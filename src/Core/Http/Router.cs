using UnityRuntimeDebugger.Api;

namespace UnityRuntimeDebugger.Http;

/// <summary>
/// Route table of the JSON API. Routes carry their own documentation, so /help/api is generated
/// from the same table that dispatches requests and cannot drift from it.
/// </summary>
internal sealed class Router
{
    private readonly List<Route> _routes = new();
    private string _group = "General";

    public IReadOnlyList<Route> Routes => _routes;

    /// <summary>Routes mapped after this call are listed under <paramref name="group"/> in the reference.</summary>
    public Router Group(string group)
    {
        _group = group;
        return this;
    }

    public Route Get(string pattern, string summary, Func<ApiContext, object?> handler) => Map("GET", pattern, summary, Sync(handler));

    public Route Get(string pattern, string summary, Func<ApiContext, Task<object?>> handler) => Map("GET", pattern, summary, handler);

    public Route Post(string pattern, string summary, Func<ApiContext, Task<object?>> handler) => Map("POST", pattern, summary, handler);

    public Route Put(string pattern, string summary, Func<ApiContext, Task<object?>> handler) => Map("PUT", pattern, summary, handler);

    public Route Patch(string pattern, string summary, Func<ApiContext, Task<object?>> handler) => Map("PATCH", pattern, summary, handler);

    public Route Delete(string pattern, string summary, Func<ApiContext, object?> handler) => Map("DELETE", pattern, summary, Sync(handler));

    public RouteMatch Match(string method, string path)
    {
        var segments = Split(path);
        var allowed = new List<string>();
        Route? best = null;
        Dictionary<string, string>? bestValues = null;
        var bestScore = -1;

        foreach (var route in _routes)
        {
            if (!route.TryBind(segments, out var values, out var score))
                continue;
            if (route.Method != method && !(method == "HEAD" && route.Method == "GET"))
            {
                allowed.Add(route.Method);
                continue;
            }

            if (score > bestScore)
            {
                best = route;
                bestValues = values;
                bestScore = score;
            }
        }

        return new RouteMatch(best, bestValues ?? new Dictionary<string, string>(), allowed.Distinct().ToList());
    }

    /// <summary>The routes closest to a path that matched nothing, for "did you mean" errors.</summary>
    public IReadOnlyList<string> Suggest(string method, string path, int count = 3)
    {
        var segments = Split(path);
        return _routes
            .Select(r => (route: r, score: r.Similarity(segments) * 2 + (r.Method == method ? 1 : 0)))
            .Where(x => x.score > 4)
            .OrderByDescending(x => x.score)
            .Take(count)
            .Select(x => $"{x.route.Method} {x.route.Pattern}")
            .ToList();
    }

    internal static string[] Split(string path) => path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private Route Map(string method, string pattern, string summary, Func<ApiContext, Task<object?>> handler)
    {
        var route = new Route(method, pattern, summary, _group, handler);
        _routes.Add(route);
        return route;
    }

    private static Func<ApiContext, Task<object?>> Sync(Func<ApiContext, object?> handler) => context => Task.FromResult(handler(context));
}

internal sealed record RouteMatch(Route? Route, IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Allowed);

internal sealed record RouteParameter(string Name, string In, string Description);

internal sealed class Route
{
    private static readonly Dictionary<string, string> CommonPathParameters = new()
    {
        ["h"] = "Object handle, as returned in a value's `handle` (`12` or `$12`).",
        ["name"] = "Member or method name.",
        ["type"] = "Full type name, e.g. `UnityEngine.Transform` (nested types use `+`).",
        ["id"] = "Hook id.",
    };

    private readonly string[] _segments;

    public Route(string method, string pattern, string summary, string group, Func<ApiContext, Task<object?>> handler)
    {
        Method = method;
        Pattern = pattern;
        Summary = summary;
        Group = group;
        Handler = handler;
        _segments = Router.Split(pattern);
        foreach (var segment in _segments.Where(IsParameter))
        {
            var name = segment.Trim('{', '}');
            Parameters.Add(new RouteParameter(name, "path", CommonPathParameters.GetValueOrDefault(name, "")));
        }
    }

    public string Method { get; }

    public string Pattern { get; }

    public string Summary { get; }

    public string Group { get; }

    public Func<ApiContext, Task<object?>> Handler { get; }

    public List<RouteParameter> Parameters { get; } = new();

    /// <summary>Example JSON body for POST/PUT/PATCH routes.</summary>
    public string? BodyExample { get; private set; }

    public string? Notes { get; private set; }

    public Route Query(string name, string description)
    {
        Parameters.Add(new RouteParameter(name, "query", description));
        return this;
    }

    public Route Body(string example, string? description = null)
    {
        BodyExample = example;
        if (description != null)
            Parameters.Add(new RouteParameter("body", "body", description));
        return this;
    }

    public Route Note(string notes)
    {
        Notes = notes;
        return this;
    }

    internal bool TryBind(string[] segments, out Dictionary<string, string> values, out int score)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        score = 0;
        if (segments.Length != _segments.Length)
            return false;

        for (var i = 0; i < segments.Length; i++)
        {
            if (IsParameter(_segments[i]))
            {
                values[_segments[i].Trim('{', '}')] = segments[i];
            }
            else if (_segments[i] == segments[i])
            {
                // literal segments beat parameters: /api/objects/search over /api/objects/{h}
                score += 1 << (segments.Length - i);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Leading segments in common with a path (parameters match anything; a near-miss typo such as
    /// "objectz" counts too), minus the difference in length.
    /// </summary>
    internal int Similarity(string[] segments)
    {
        var shared = 0;
        while (shared < segments.Length && shared < _segments.Length
               && (IsParameter(_segments[shared]) || _segments[shared] == segments[shared] || Distance(_segments[shared], segments[shared]) <= 2))
            shared++;
        return shared * 2 - Math.Abs(segments.Length - _segments.Length);
    }

    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }

        return row[b.Length];
    }

    private static bool IsParameter(string segment) => segment.StartsWith('{') && segment.EndsWith('}');
}
