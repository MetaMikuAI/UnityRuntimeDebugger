using System.Reflection;
using System.Text;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Hooks;

public enum HookAction
{
    /// <summary>Record calls; the original runs as usual.</summary>
    Log,

    /// <summary>Record calls and skip the original.</summary>
    Skip,

    /// <summary>Record calls, skip the original and return a fixed value.</summary>
    Return,

    /// <summary>User-written patch source.</summary>
    Custom,
}

/// <summary>
/// Generates the Harmony patch class for a hook. The result is ordinary C# the user can edit:
/// typed parameters (<c>__instance</c>, <c>__0</c>..., <c>__result</c>) so no reflection runs per call.
/// </summary>
internal static class HookSource
{
    public static string ClassName(int id) => "UrdbgHook" + id;

    public static string Generate(int id, MethodBase method, HookAction action, string? returnValue)
    {
        var declaring = method.DeclaringType!;
        var instance = !method.IsStatic && !declaring.IsValueType ? $"{Name(declaring)} __instance" : null;
        var args = method.GetParameters().Select((p, i) => (p, decl: Parameter(p, i))).ToList();
        var returnType = method is MethodInfo info && info.ReturnType != typeof(void) ? info.ReturnType : null;
        var result = returnType != null && Nameable(returnType) ? Name(returnType) : null;

        if (action == HookAction.Return && result == null)
            throw new InvalidOperationException(returnType == null ? "the method returns nothing; use skip instead of return" : $"cannot name {TypeNames.Display(returnType)} in C#");
        if (action is HookAction.Skip or HookAction.Return && method is ConstructorInfo)
            throw new InvalidOperationException("constructors can only be logged");

        var declared = args.Select(a => a.decl).ToList();
        var prefixParams = Parameters(instance, declared, action == HookAction.Return ? $"ref {result} __result" : null, "out long __state");
        var postfixParams = Parameters(instance, declared, result != null ? $"{result} __result" : null, "long __state");
        var captured = string.Join(", ", args.Select((a, i) => a.decl == null ? "null" : "__" + i));

        var body = action switch
        {
            HookAction.Skip => "        return false; // skip the original\n",
            HookAction.Return => $"        __result = ({result})({returnValue});\n        return false; // skip the original, return __result\n",
            _ => "        return true;\n",
        };

        return new StringBuilder()
            .Append("// Hook #").Append(id).Append(": ").Append(MethodTable.Ref(method)).Append('\n')
            .Append("// Harmony applies the Prefix, Postfix, Finalizer and Transpiler methods of this class. Edit freely;\n")
            .Append("// HookContext records calls for the Hooks panel and /api/hooks/").Append(id).Append("/calls.\n")
            .Append("using System;\n")
            .Append("using UnityRuntimeDebugger.Hooks;\n\n")
            .Append("public static class ").Append(ClassName(id)).Append("\n{\n")
            .Append("    static readonly HookContext Hook = HookContext.Get(").Append(id).Append(");\n\n")
            .Append("    public static bool Prefix(").Append(prefixParams).Append(")\n    {\n")
            .Append("        __state = Hook.Enter(").Append(instance != null ? "__instance" : "null").Append(");\n")
            .Append("        if (__state < 0)\n")
            .Append("            return true; // reached through a folded (ICF) method or while recording: leave it alone\n")
            .Append(body)
            .Append("    }\n\n")
            .Append("    public static void Postfix(").Append(postfixParams).Append(")\n    {\n")
            .Append("        if (__state > 0)\n")
            .Append("            Hook.Exit(__state, ").Append(instance != null ? "__instance" : "null")
            .Append(", new object[] { ").Append(captured).Append(" }, ").Append(result != null ? "__result, true" : "null, false").Append(");\n")
            .Append("    }\n\n")
            .Append("    public static Exception Finalizer(Exception __exception, long __state) => Hook.Fail(__state, __exception);\n")
            .Append("}\n")
            .ToString();
    }

    /// <summary>
    /// Harmony binds patch parameters by name (<c>__0</c> is the first argument). Types C# cannot
    /// name become <c>object</c>; pointers and unnameable structs are left out and logged as null.
    /// </summary>
    private static string? Parameter(ParameterInfo parameter, int index)
    {
        var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        if (type.IsPointer || type.IsValueType && !Nameable(type))
            return null;
        var name = Nameable(type) ? Name(type) : "object";
        return (parameter.ParameterType.IsByRef ? "ref " : "") + name + " __" + index;
    }

    private static bool Nameable(Type type) => TypeNames.NameableType(type) == type || type.IsPrimitive || type == typeof(string) || type == typeof(object);

    private static string Name(Type type) => TypeNames.CSharp(type);

    private static string Parameters(string? instance, IEnumerable<string?> args, string? result, string state)
        => string.Join(", ", new[] { instance }.Concat(args).Append(result).Append(state).Where(p => p != null));
}
