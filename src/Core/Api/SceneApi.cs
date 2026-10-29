using UnityRuntimeDebugger.Http;

namespace UnityRuntimeDebugger.Api;

internal static class SceneApi
{
    public static void Map(Router router)
    {
        router.Group("Scenes");

        router.Get("/api/scenes", "Loaded scenes and their root GameObjects; the last one is DontDestroyOnLoad.",
            c => c.Main(() => c.Debugger.Inspector.Scenes()));

        router.Get("/api/objects/search", "GameObjects whose name contains `q`, across all scenes, with their hierarchy path.",
                c => c.Main(() => c.Debugger.Inspector.Search(c.RequiredQuery("q"), c.Int("limit", 200, 1, 2000))))
            .Query("q", "Part of the name, case-insensitive.")
            .Query("limit", "Maximum results (default 200).");

        router.Get("/api/objects/{h}/children", "Child GameObjects of a GameObject (or of a component's GameObject).",
            c => c.Main(() => c.Debugger.Inspector.Children(c.Alive())));

        router.Get("/api/objects/{h}/components", "Components of a GameObject. `enabled` is set for Behaviours and Renderers; missing scripts have `missing: true`.",
            c => c.Main(() => c.Debugger.Inspector.Components(c.Alive())));
    }
}
