using System.Reflection;
using UnityRuntimeDebugger.Http;

namespace UnityRuntimeDebugger.Web;

/// <summary>
/// Files embedded at build time: the web UI (web/dist), the CLI script and the help pages. The UI
/// holds no secrets, so it is served without a token; the page asks for one itself.
/// </summary>
internal static class StaticFiles
{
    private const string WebPrefix = "urdbg/web/";

    // The page only talks to this origin: no inline scripts, no framing.
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "connect-src 'self'; font-src 'self' data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private static readonly Assembly Assembly = typeof(StaticFiles).Assembly;

    // Resource names keep the build machine's separators (assets\x.js on Windows).
    private static readonly Lazy<Dictionary<string, string>> Resources = new(() => Assembly.GetManifestResourceNames()
        .ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.Ordinal));

    private static readonly Dictionary<string, HttpResponse?> Cache = new(StringComparer.Ordinal);

    public static HttpResponse? Get(string path)
    {
        if (path == "/")
            path = "/index.html";

        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var cached))
                return cached;
        }

        var response = path == "/urdbg.py" ? Load("urdbg/cli/urdbg.py", "text/x-python; charset=utf-8") : Load(WebPrefix + path.TrimStart('/'), null);
        if (response != null && path == "/index.html")
        {
            response.WithHeader("Content-Security-Policy", ContentSecurityPolicy)
                .WithHeader("Referrer-Policy", "no-referrer")
                .WithHeader("X-Frame-Options", "DENY");
        }
        else if (response != null && path.StartsWith("/assets/", StringComparison.Ordinal))
        {
            // Vite names assets by content hash.
            response.WithHeader("Cache-Control", "public, max-age=31536000, immutable");
        }

        lock (Cache)
            Cache[path] = response;
        return response;
    }

    public static string? Text(string resource)
    {
        if (!Resources.Value.TryGetValue(resource, out var name))
            return null;
        using var stream = Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IEnumerable<string> Names(string prefix) => Resources.Value.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));

    private static HttpResponse? Load(string resource, string? contentType)
    {
        if (resource.Contains("..") || !Resources.Value.TryGetValue(resource, out var name))
            return null;

        using var stream = Assembly.GetManifestResourceStream(name)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return HttpResponse.Cached(memory.ToArray(), contentType ?? MimeType(resource));
    }

    private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };
}
