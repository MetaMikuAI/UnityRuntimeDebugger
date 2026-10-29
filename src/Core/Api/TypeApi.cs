using System.Reflection;
using UnityRuntimeDebugger.Http;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Api;

internal static class TypeApi
{
    public static void Map(Router router)
    {
        router.Group("Types");

        router.Get("/api/assemblies", "Loaded assemblies with public types; `group` is game, unity, il2cpp, mods or bcl.",
            _ => TypeCatalog.Assemblies().Select(a => new { name = a.Name, types = a.Types, native = a.Native ? true : (bool?)null, group = a.Group }));

        router.Get("/api/assemblies/{name}/types", "Public types of one assembly, grouped by namespace.",
            c => TypeCatalog.TypesIn(c.Route("name"))
                .GroupBy(t => t.Namespace)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { @namespace = g.Key, types = g.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(Row).ToList() }));

        router.Get("/api/types", "Search types by name: exact, prefix, CamelCase initials (`gom` finds GameObjectManager), substring.",
                c => TypeCatalog.Search(c.RequiredQuery("q"), c.Int("limit", 50, 1, 500)).Select(Row))
            .Query("q", "Short name, initials, or a dotted full name.")
            .Query("limit", "Maximum results (default 50).");

        router.Get("/api/types/{type}", "A type by full (or unique short) name: its info and a handle for its static side.", c =>
        {
            var type = Find(c.Route("type"));
            return new { handle = c.Debugger.Objects.Register(StaticView.Of(type)), type = c.Debugger.Inspector.TypeOf(type) };
        });

        router.Get("/api/types/{type}/instances", "Loaded instances of a UnityEngine.Object type (scene objects and assets).", c =>
            {
                var type = Find(c.Route("type"));
                if (!c.Debugger.Game.IsUnityObjectType(type))
                    throw ApiException.BadRequest($"{type.FullName} is not a UnityEngine.Object type");
                return c.Main(() => c.Debugger.Inspector.Instances(type, c.Int("limit", 500, 1, 5000)));
            })
            .Query("limit", "Maximum instances returned (default 500); `total` counts all.");

        router.Get("/api/methods", "Resolve a method reference: overloads, whether each can be hooked, native address and ICF-folded methods.",
                c => c.Debugger.Hooks.Candidates(c.RequiredQuery("q")))
            .Query("q", "`Namespace.Type::Method`, optionally `Type::Method(int, string)`.");
    }

    private static Type Find(string name)
    {
        try
        {
            return TypeCatalog.Find(name) ?? throw ApiException.NotFound($"type not found: {name}", "/help/types");
        }
        catch (AmbiguousMatchException ex)
        {
            throw ApiException.BadRequest(ex.Message, "/help/types");
        }
    }

    private static object Row(TypeCatalog.TypeEntry t) => new
    {
        name = t.Name,
        fullName = t.FullName,
        @namespace = t.Namespace,
        assembly = t.Assembly,
        kind = t.Kind,
        unity = t.Unity ? true : (bool?)null,
        native = t.Native ? true : (bool?)null,
    };
}
