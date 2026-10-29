using System.Text;
using UnityRuntimeDebugger.Http;
using UnityRuntimeDebugger.Inspection;
using UnityRuntimeDebugger.Web;

namespace UnityRuntimeDebugger.Help;

/// <summary>
/// /help: embedded Markdown pages with <c>{{base}}</c> filled in from the request, so examples run
/// as copied; the API reference is generated from the route table.
/// </summary>
internal sealed class HelpPages
{
    private const string Markdown = "text/markdown; charset=utf-8";
    private const string Prefix = "urdbg/help/";

    private readonly Router _router;

    public HelpPages(Router router) => _router = router;

    public IReadOnlyList<string> Topics
        => StaticFiles.Names(Prefix).Select(n => n.Substring(Prefix.Length, n.Length - Prefix.Length - 3)).Where(t => t != "index").Append("api").OrderBy(t => t).ToList();

    public HttpResponse Render(string topic, string baseUrl, Debugger? live)
    {
        topic = topic.ToLowerInvariant();
        var index = topic is "" or "index";
        var text = topic == "api" ? ApiReference() : StaticFiles.Text(Prefix + (index ? "index" : topic) + ".md");
        if (text == null)
            return HttpResponse.Text($"# Not found\n\nNo help topic '{topic}'. Topics: {string.Join(", ", Topics.Select(t => $"/help/{t}"))}\n", Markdown, 404);

        text = text.Replace("{{base}}", baseUrl).Replace("{{version}}", Debugger.Version);
        if (index && live != null)
            text = InsertAfterTitle(text, LiveSection(live));
        return HttpResponse.Text(text, Markdown);
    }

    /// <summary>Facts about the connected game.</summary>
    private static string LiveSection(Debugger debugger)
    {
        var sb = new StringBuilder("## Connected game\n\n");
        try
        {
            var game = debugger.MainThread.Run(() => debugger.Game.Status(), TimeSpan.FromSeconds(2));
            sb.Append($"- {game.Product} {game.Version} · Unity {game.Unity} · {game.Platform} · {debugger.Runtime.Name} · {debugger.Options.Loader.Name}\n");
            sb.Append($"- Scenes: {string.Join(", ", game.Scenes)} (active: {game.ActiveScene})\n");
        }
        catch (TimeoutException)
        {
            sb.Append("- The main thread is not responding right now (loading?); engine routes will time out until it does.\n");
        }

        var assemblies = TypeCatalog.Assemblies().Where(a => a.Group == "game").Select(a => $"{a.Name} ({a.Types} types)").ToList();
        if (assemblies.Count > 0)
            sb.Append($"- Game assemblies: {string.Join(", ", assemblies)}\n");
        if (debugger.Hooks.Count > 0)
            sb.Append($"- Active hooks: {debugger.Hooks.Count} (GET /api/hooks)\n");
        return sb.Append('\n').ToString();
    }

    private string ApiReference()
    {
        var sb = new StringBuilder()
            .Append("# API reference\n\n")
            .Append("Every route needs `Authorization: Bearer <token>`. Bodies are JSON (any Content-Type). Errors are ")
            .Append("`{\"error\": {\"code\", \"message\", \"help\", \"details\"}}` with a matching HTTP status. ")
            .Append("Values everywhere have the same shape, see /help/objects.\n");

        foreach (var group in _router.Routes.GroupBy(r => r.Group))
        {
            sb.Append("\n## ").Append(group.Key).Append('\n');
            foreach (var route in group)
            {
                sb.Append("\n### ").Append(route.Method).Append(' ').Append(route.Pattern).Append("\n\n").Append(route.Summary).Append('\n');
                foreach (var p in route.Parameters.Where(p => p.Description.Length > 0))
                    sb.Append("- `").Append(p.Name).Append("` (").Append(p.In).Append("): ").Append(p.Description).Append('\n');
                if (route.BodyExample != null)
                    sb.Append("\nBody: `").Append(route.BodyExample).Append("`\n");
                if (route.Notes != null)
                    sb.Append('\n').Append(route.Notes).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string InsertAfterTitle(string text, string section)
    {
        var end = text.IndexOf("\n\n", StringComparison.Ordinal);
        return end < 0 ? text + "\n\n" + section : text.Substring(0, end + 2) + section + text.Substring(end + 2);
    }
}
