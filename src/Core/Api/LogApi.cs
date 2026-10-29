using UnityRuntimeDebugger.Http;

namespace UnityRuntimeDebugger.Api;

internal static class LogApi
{
    public static void Map(Router router)
    {
        router.Group("Logs");

        router.Get("/api/logs", "Recent log entries of all sources. Tail with the returned `cursor`.", async c =>
            {
                var page = await c.Debugger.Logs.Feed.WaitAsync(c.Long("after", -1), c.Int("limit", 1000, 1, 5000), c.Wait(), c.Aborted);
                return new { entries = page.Items, cursor = page.Cursor, dropped = page.Dropped };
            })
            .Query("after", "Cursor from the previous response; omit for the newest entries.")
            .Query("limit", "Maximum entries (default 1000).")
            .Query("wait", "Seconds to wait for new entries when there are none (0–30, default 0).");

        router.Get("/api/logs/file", "The loader's whole log file as plain text.", c =>
        {
            var path = c.Debugger.Options.LogFile;
            if (path == null || !File.Exists(path))
                throw ApiException.NotFound($"log file not found{(path == null ? "" : ": " + path)}");

            // The loader keeps the file open for writing.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            file.CopyTo(memory);
            return HttpResponse.Bytes(memory.ToArray(), "text/plain; charset=utf-8");
        });
    }
}
