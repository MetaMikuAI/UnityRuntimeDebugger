using System.Text.Json;
using UnityRuntimeDebugger.Http;

namespace UnityRuntimeDebugger.Api;

/// <summary>One API request: route values, query and body parsing, and access to the debugger.</summary>
internal sealed class ApiContext
{
    private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(15);

    private readonly IReadOnlyDictionary<string, string> _route;

    public ApiContext(Debugger debugger, HttpRequest request, IReadOnlyDictionary<string, string> route, CancellationToken aborted)
    {
        Debugger = debugger;
        Request = request;
        _route = route;
        Aborted = aborted;
    }

    public Debugger Debugger { get; }

    public HttpRequest Request { get; }

    public CancellationToken Aborted { get; }

    public string Route(string name) => _route[name];

    /// <summary>A handle from the route: <c>12</c> or <c>$12</c>.</summary>
    public int Handle(string name = "h")
    {
        var text = Route(name).TrimStart('$');
        return int.TryParse(text, out var handle) ? handle : throw ApiException.BadRequest($"'{Route(name)}' is not a handle; use the number from a value's \"handle\"", "/help/objects");
    }

    /// <summary>The object behind the route's handle.</summary>
    public object Target(string name = "h")
    {
        var handle = Handle(name);
        return Debugger.Objects.TryGet(handle, out var value)
            ? value
            : throw ApiException.NotFound($"${handle} has expired or never existed; handles are dropped when the table is cleared or the game restarts", "/help/objects#handles");
    }

    /// <summary><see cref="Target"/>, refusing Unity objects that were destroyed. Main thread only.</summary>
    public object Alive(string name = "h")
    {
        var target = Target(name);
        return Debugger.Game.IsDestroyed(target)
            ? throw new ApiException(410, "destroyed", $"${Handle(name)} was destroyed (scene unloaded or Destroy called)", "/help/objects#handles")
            : target;
    }

    public string? Query(string name) => Request.Query.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    public string RequiredQuery(string name) => Query(name) ?? throw ApiException.BadRequest($"missing query parameter '{name}'");

    public int Int(string name, int fallback, int min = int.MinValue, int max = int.MaxValue)
        => Query(name) is { } text ? int.TryParse(text, out var value) ? Math.Clamp(value, min, max) : throw ApiException.BadRequest($"'{name}' must be a number") : fallback;

    public long Long(string name, long fallback)
        => Query(name) is { } text ? long.TryParse(text, out var value) ? value : throw ApiException.BadRequest($"'{name}' must be a number") : fallback;

    public bool Flag(string name) => Query(name) is "1" or "true";

    /// <summary>Long-poll wait from <c>?wait=seconds</c>, capped at 30 s.</summary>
    public TimeSpan Wait() => TimeSpan.FromSeconds(Int("wait", 0, 0, 30));

    public async Task<string> Text() => await Request.ReadTextAsync(Aborted);

    public async Task<T> Body<T>() where T : class
    {
        var text = await Text();
        if (string.IsNullOrWhiteSpace(text))
            throw ApiException.BadRequest("expected a JSON body");
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json.Options) ?? throw ApiException.BadRequest("expected a JSON object");
        }
        catch (JsonException ex)
        {
            throw ApiException.BadRequest("invalid JSON: " + ex.Message);
        }
    }

    public T Main<T>(Func<T> func) => Debugger.MainThread.Run(func, MainThreadTimeout);
}
