using System.Globalization;
using System.Reflection;
using System.Text.Json;
using UnityRuntimeDebugger.Hosting;

namespace UnityRuntimeDebugger.Inspection;

public sealed class ParameterView
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public Editor? Editor { get; init; }
    public string[]? Options { get; init; }
    public bool? Optional { get; init; }
    public string? Default { get; init; }
}

public sealed class MethodView
{
    /// <summary>Index in the target's method list; stable for a given type.</summary>
    public int Id { get; init; }

    public string Name { get; init; } = "";

    /// <summary><c>Name(int, string)</c></summary>
    public string Signature { get; init; } = "";

    public string ReturnType { get; init; } = "";
    public List<ParameterView> Parameters { get; init; } = new();
    public bool? Static { get; init; }
    public string DeclaringType { get; init; } = "";
    public int Depth { get; init; }

    /// <summary>False for open generics and by-ref / pointer parameters, which cannot be called from the UI.</summary>
    public bool Invocable { get; init; }

    /// <summary>Method reference for hooks, <c>Namespace.Type::Name(int, string)</c>.</summary>
    public string Ref { get; init; } = "";
}

/// <summary>Public methods of objects and static types, and calling them with client-supplied arguments.</summary>
public sealed class MethodTable
{
    private static readonly Dictionary<(Type, bool), List<MethodInfo>> Cache = new();

    private readonly IRuntime _runtime;
    private readonly ValueParser _parser;

    public MethodTable(IRuntime runtime, ValueParser parser)
    {
        _runtime = runtime;
        _parser = parser;
    }

    public List<MethodView> List(object target)
    {
        var (type, instance) = MemberReader.Target(target);
        return Methods(type, instance == null).Select((m, i) => View(type, m, i)).ToList();
    }

    /// <summary>
    /// Calls a method by list id or by name. With overloads, the one whose arity matches the
    /// arguments wins; anything still ambiguous is an error that lists the candidates.
    /// </summary>
    public (object? result, bool isVoid) Invoke(object target, string method, JsonElement[] args)
    {
        var (type, instance) = MemberReader.Target(target);
        var methods = Methods(type, instance == null);

        MethodInfo chosen;
        if (int.TryParse(method, out var id))
        {
            chosen = id >= 0 && id < methods.Count ? methods[id] : throw new MissingMethodException($"no method with id {id}");
        }
        else
        {
            var named = methods.Where(m => m.Name == method).ToList();
            if (named.Count == 0)
                throw new MissingMethodException($"{TypeNames.Display(type)} has no public method '{method}'");

            var candidates = named.Where(m => m.GetParameters().Length == args.Length).ToList();
            if (candidates.Count != 1)
                candidates = named.Where(m => Fits(m, args.Length)).ToList();
            if (candidates.Count != 1)
                throw new AmbiguousMatchException($"'{method}' has {named.Count} overloads; call one by id: "
                    + string.Join("; ", named.Select(m => $"{methods.IndexOf(m)} = {Signature(m)}")));
            chosen = candidates[0];
        }

        var parameters = chosen.GetParameters();
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i < args.Length && args[i].ValueKind != JsonValueKind.Undefined)
                values[i] = _parser.Convert(args[i], parameters[i].ParameterType);
            else if (parameters[i].HasDefaultValue)
                values[i] = parameters[i].DefaultValue;
            else
                throw new ArgumentException($"missing argument '{parameters[i].Name}'");
        }

        return (chosen.Invoke(instance, values), chosen.ReturnType == typeof(void));
    }

    public static string Signature(MethodBase method)
        => method.Name + "(" + string.Join(", ", method.GetParameters().Select(p => ParameterType(p))) + ")";

    /// <summary><c>Namespace.Outer+Inner::Name(int, string)</c></summary>
    public static string Ref(MethodBase method)
        => (method.DeclaringType?.FullName ?? "?") + "::" + Signature(method);

    private static string ParameterType(ParameterInfo parameter)
        => (parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "") + TypeNames.Display(parameter.ParameterType);

    private static bool Fits(MethodInfo method, int argCount)
    {
        var parameters = method.GetParameters();
        return argCount <= parameters.Length && parameters.Skip(argCount).All(p => p.HasDefaultValue);
    }

    private MethodView View(Type type, MethodInfo method, int id)
    {
        var parameters = method.GetParameters().Select(p =>
        {
            var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
            return new ParameterView
            {
                Name = p.Name ?? "arg" + p.Position,
                Type = ParameterType(p),
                Editor = ValueParser.EditorFor(type, out var options),
                Options = options,
                Optional = p.HasDefaultValue ? true : null,
                Default = p.HasDefaultValue ? p.DefaultValue == null ? "null" : Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) : null,
            };
        }).ToList();

        return new MethodView
        {
            Id = id,
            Name = method.Name,
            Signature = Signature(method),
            ReturnType = TypeNames.Display(method.ReturnType),
            Parameters = parameters,
            Static = method.IsStatic ? true : null,
            DeclaringType = TypeNames.Display(method.DeclaringType!),
            Depth = Depth(type, method.DeclaringType!),
            Invocable = !method.ContainsGenericParameters && method.GetParameters().All(p => !p.ParameterType.IsByRef && !p.ParameterType.IsPointer),
            Ref = Ref(method),
        };
    }

    private List<MethodInfo> Methods(Type type, bool statics)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((type, statics), out var cached))
                return cached;
        }

        var flags = BindingFlags.Public | (statics ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
        var list = type.GetMethods(flags)
            .Where(m => !m.IsSpecialName && m.DeclaringType != null && !_runtime.IsRootType(m.DeclaringType) && !m.Name.Contains('<'))
            // interop explicit-interface stubs
            .Where(m => !m.Name.StartsWith("System_", StringComparison.Ordinal) && !m.Name.StartsWith("Il2CppSystem_", StringComparison.Ordinal))
            .OrderBy(m => Depth(type, m.DeclaringType!))
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.GetParameters().Length)
            .ToList();

        lock (Cache)
            Cache[(type, statics)] = list;
        return list;
    }

    private static int Depth(Type type, Type declaring)
    {
        var depth = 0;
        for (var t = type; t != null && t != declaring; t = t.BaseType)
            depth++;
        return depth;
    }
}
