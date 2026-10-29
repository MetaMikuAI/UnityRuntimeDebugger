using UnityRuntimeDebugger.Http;

namespace UnityRuntimeDebugger.Api;

internal static class StatusApi
{
    private static readonly TimeSpan GameTimeout = TimeSpan.FromSeconds(3);

    public static void Map(Router router)
    {
        router.Group("Status");

        router.Get("/api/status", "Game, engine and debugger status. `game` is null while the main thread does not respond.", c =>
        {
            var d = c.Debugger;
            object? game = null;
            try
            {
                game = d.MainThread.Run(() => d.Game.Status(), GameTimeout);
            }
            catch (TimeoutException)
            {
                // reported as game: null, mainThread: false
            }

            return new
            {
                game,
                mainThread = game != null,
                debugger = new
                {
                    version = Debugger.Version,
                    boot = d.Boot,
                    runtime = d.Runtime.Name,
                    loader = new { name = d.Options.Loader.Name, version = d.Options.Loader.Version },
                    plugins = d.Options.Loader.Plugins(),
                    handles = d.Objects.Count,
                    hooks = d.Hooks.Count,
                    managedMb = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
                    workingSetMb = Math.Round(Environment.WorkingSet / 1048576.0, 1),
                },
            };
        });
    }
}
