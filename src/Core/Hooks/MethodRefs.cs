using System.Reflection;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Hooks;

/// <summary>
/// Method references as text: <c>Namespace.Type::Name</c>, or <c>Namespace.Type::Name(int, string)</c>
/// to pick an overload. Constructors are <c>.ctor</c>. The type may be a short name if it is unique.
/// </summary>
internal static class MethodRefs
{
    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>All methods the reference can mean (one, unless the overload was left out).</summary>
    public static List<MethodBase> Resolve(string reference)
    {
        reference = reference.Trim();
        var separator = reference.IndexOf("::", StringComparison.Ordinal);
        var paren = reference.IndexOf('(');
        string typeName, rest;
        if (separator >= 0)
        {
            typeName = reference.Substring(0, separator);
            rest = reference.Substring(separator + 2);
        }
        else
        {
            var dot = (paren < 0 ? reference : reference.Substring(0, paren)).LastIndexOf('.');
            if (dot <= 0)
                throw new FormatException($"expected Type::Method, got '{reference}'");
            typeName = reference.Substring(0, dot);
            rest = reference.Substring(dot + 1);
        }

        var type = TypeCatalog.Find(typeName) ?? throw new TypeLoadException($"type not found: {typeName}");
        var open = rest.IndexOf('(');
        var name = (open < 0 ? rest : rest.Substring(0, open)).Trim();
        var signature = open < 0 ? null : Normalize(name + rest.Substring(open));

        var candidates = Members(type).Where(m => m.Name == name).ToList();
        if (candidates.Count == 0)
        {
            var inherited = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == name);
            throw new MissingMethodException(inherited?.DeclaringType != null
                ? $"{name} is declared on {inherited.DeclaringType.FullName}; hook {MethodTable.Ref(inherited)}"
                : $"{type.FullName} has no method '{name}'");
        }

        if (signature == null)
            return candidates;

        var exact = candidates.Where(m => Normalize(MethodTable.Signature(m)) == signature).ToList();
        return exact.Count > 0
            ? exact
            : throw new MissingMethodException($"no overload {signature}; candidates: " + string.Join(", ", candidates.Select(MethodTable.Signature)));
    }

    public static MethodBase ResolveOne(string reference)
    {
        var methods = Resolve(reference);
        return methods.Count == 1
            ? methods[0]
            : throw new AmbiguousMatchException($"{methods.Count} overloads, add the parameter list: " + string.Join(", ", methods.Select(MethodTable.Ref)));
    }

    private static IEnumerable<MethodBase> Members(Type type)
        => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));

    private static string Normalize(string signature) => string.Concat(signature.Where(c => !char.IsWhiteSpace(c)));
}
