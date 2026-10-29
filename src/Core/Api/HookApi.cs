using UnityRuntimeDebugger.Hooks;
using UnityRuntimeDebugger.Http;

namespace UnityRuntimeDebugger.Api;

internal static class HookApi
{
    public static void Map(Router router)
    {
        router.Group("Hooks");

        router.Get("/api/hooks", "All hooks with their call counters.", c => c.Debugger.Hooks.List());

        router.Post("/api/hooks", "Hook a method: patch it with Harmony and record its calls.", async c => c.Debugger.Hooks.Create(await c.Body<HookRequest>()))
            .Body("{\"method\": \"UnityEngine.Transform::set_position\", \"action\": \"log\"}",
                "`method`; `action`: `log`, `skip` or `return` (with `returnValue`); `source`; `enabled`; `acceptFolded`.");

        router.Get("/api/hooks/{id}", "One hook, with its patch source.", c => c.Debugger.Hooks.Get(Id(c)));

        router.Patch("/api/hooks/{id}", "Enable or disable a hook, change its action, or replace its source.", async c => c.Debugger.Hooks.Update(Id(c), await c.Body<HookUpdate>()))
            .Body("{\"enabled\": false}", "Any of `enabled`, `action`, `returnValue`, `source`.");

        router.Delete("/api/hooks/{id}", "Remove a hook (unpatch it).", c =>
        {
            c.Debugger.Hooks.Delete(Id(c));
            return new { ok = true };
        });

        router.Get("/api/hooks/{id}/calls", "Recorded calls: arguments, instance, result, exception, duration. Tail with `cursor`.",
                async c => await c.Debugger.Hooks.Calls(Id(c), c.Long("after", -1), c.Int("limit", 100, 1, 500), c.Wait(), c.Aborted))
            .Query("after", "Cursor from the previous response; omit for the newest calls.")
            .Query("limit", "Maximum calls (default 100).")
            .Query("wait", "Seconds to wait for a call when there are none (0–30).");

        router.Delete("/api/hooks/{id}/calls", "Clear a hook's recorded calls.", c =>
        {
            c.Debugger.Hooks.ClearCalls(Id(c));
            return new { ok = true };
        });
    }

    private static int Id(ApiContext c)
        => int.TryParse(c.Route("id").TrimStart('#'), out var id) ? id : throw ApiException.BadRequest("hook ids are numbers");
}
