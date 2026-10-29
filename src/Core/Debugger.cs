using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityRuntimeDebugger.Api;
using UnityRuntimeDebugger.Help;
using UnityRuntimeDebugger.Hooks;
using UnityRuntimeDebugger.Hosting;
using UnityRuntimeDebugger.Http;
using UnityRuntimeDebugger.Inspection;
using UnityRuntimeDebugger.Logging;
using UnityRuntimeDebugger.Scripting;
using UnityRuntimeDebugger.Web;

namespace UnityRuntimeDebugger;

/// <summary>
/// The running debugger: wires the services together and serves the API, the web UI and /help.
/// A loader adapter starts one, pumps <see cref="MainThread"/> every frame and feeds <see cref="Logs"/>.
/// </summary>
public sealed class Debugger : IDisposable
{
    public static readonly string Version = typeof(Debugger).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly byte[] _tokenHash;
    private readonly Router _router = new();
    private readonly HelpPages _help;
    private readonly HttpServer _server;

    private Debugger(DebuggerOptions options)
    {
        Options = options;
        _tokenHash = Hash(options.Token);

        Objects = new ObjectTable(Runtime);
        var values = new Values(Runtime, Game, Objects);
        var parser = new ValueParser(Runtime, Objects);
        Inspector = new Inspector(Game, Runtime, Objects, values, new MemberReader(Runtime, values, parser), new CollectionReader(values), new MethodTable(Runtime, parser));
        Scripts = new ScriptEngine(MainThread, Objects, values);
        Hooks = new HookManager(MainThread, Runtime, values, Objects);

        StatusApi.Map(_router);
        SceneApi.Map(_router);
        ObjectApi.Map(_router);
        TypeApi.Map(_router);
        EvalApi.Map(_router);
        HookApi.Map(_router);
        LogApi.Map(_router);
        _help = new HelpPages(_router);

        _server = new HttpServer(IPAddress.Parse(options.ListenAddress), options.Port, HandleAsync, options.OnError);
    }

    /// <summary>The running instance, for code compiled at runtime (handles, hook patches).</summary>
    public static Debugger? Current { get; private set; }

    public DebuggerOptions Options { get; }

    public IGame Game => Options.Game;

    public IRuntime Runtime => Options.Runtime;

    public MainThread MainThread { get; } = new();

    public LogBuffer Logs { get; } = new();

    /// <summary>Changes on every start, so clients can tell their handles are from an earlier run.</summary>
    public string Boot { get; } = Guid.NewGuid().ToString("N");

    public int Port => _server.EndPoint.Port;

    internal ObjectTable Objects { get; }

    internal Inspector Inspector { get; }

    internal ScriptEngine Scripts { get; }

    internal HookManager Hooks { get; }

    internal IReadOnlyList<Route> Routes => _router.Routes;

    public static Debugger Start(DebuggerOptions options)
    {
        if (Current != null)
            throw new InvalidOperationException("the debugger is already running");

        var debugger = new Debugger(options);
        debugger._server.Start();
        Current = debugger;
        TypeCatalog.Prewarm();
        return debugger;
    }

    /// <summary>
    /// Addresses the server can be reached at; a wildcard listener lists loopback and the LAN
    /// IPv4 addresses. Each opens the web console as <c>{url}/#token={token}</c>.
    /// </summary>
    public IReadOnlyList<string> Urls()
    {
        var address = IPAddress.Parse(Options.ListenAddress);
        var hosts = new List<string>();
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            hosts.Add("127.0.0.1");
            try
            {
                hosts.AddRange(NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus != OperationalStatus.Down)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Select(a => a.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString())
                    .Distinct());
            }
            catch
            {
                // interface enumeration is not available everywhere
            }
        }
        else
        {
            hosts.Add(address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString());
        }

        return hosts.Select(h => $"http://{h}:{Port}").ToList();
    }

    public bool IsLoopbackOnly => IPAddress.IsLoopback(IPAddress.Parse(Options.ListenAddress));

    public void Dispose()
    {
        Hooks.Dispose();
        _server.Dispose();
        if (Current == this)
            Current = null;
    }

    private async Task<HttpResponse> HandleAsync(HttpRequest request, CancellationToken aborted)
    {
        var path = request.Path;
        if (path == "/api" || path.StartsWith("/api/", StringComparison.Ordinal))
            return await HandleApiAsync(request, aborted);

        if (request.Method is not ("GET" or "HEAD"))
            return Failure(new ApiException(405, "method_not_allowed", $"{request.Method} is only accepted under /api", "/help"));

        var baseUrl = "http://" + (request.Header("Host") ?? "127.0.0.1:" + Port);
        if (path == "/help" || path.StartsWith("/help/", StringComparison.Ordinal))
            return _help.Render(path.Substring("/help".Length).Trim('/'), baseUrl, IsAuthorized(request) ? this : null);

        // Browsers get the web console, other clients the quickstart.
        if (path == "/" && request.Header("Accept")?.Contains("text/html", StringComparison.OrdinalIgnoreCase) != true)
            return _help.Render("", baseUrl, IsAuthorized(request) ? this : null);

        return StaticFiles.Get(path)
               ?? HttpResponse.Text($"Not found: {path}\nSee {baseUrl}/help\n", status: 404);
    }

    private async Task<HttpResponse> HandleApiAsync(HttpRequest request, CancellationToken aborted)
    {
        if (!IsAuthorized(request))
        {
            return Failure(new ApiException(401, "unauthorized",
                "send the header 'Authorization: Bearer <token>'. The token is printed in the loader log at startup "
                + "(search for 'UnityRuntimeDebugger token') or set in the config as [Security] Token.", "/help"));
        }

        if (request.Path.TrimEnd('/') == "/api")
            return HttpResponse.Json(new { routes = _router.Routes.Select(r => $"{r.Method} {r.Pattern}"), help = "/help/api" });

        var match = _router.Match(request.Method, request.Path);
        if (match.Route == null)
        {
            return match.Allowed.Count > 0
                ? Failure(new ApiException(405, "method_not_allowed", $"{request.Path} accepts {string.Join(", ", match.Allowed)}", "/help/api"))
                : Failure(ApiException.NotFound($"no route {request.Method} {request.Path}", "/help/api"), new { suggestions = _router.Suggest(request.Method, request.Path) });
        }

        try
        {
            var result = await match.Route.Handler(new ApiContext(this, request, match.Values, aborted));
            return result as HttpResponse ?? HttpResponse.Json(result);
        }
        catch (Exception ex)
        {
            return Failure(ex);
        }
    }

    private HttpResponse Failure(Exception exception, object? details = null)
    {
        var ex = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
        var error = ex switch
        {
            ApiException api => api,
            TimeoutException => new ApiException(503, "main_thread_timeout",
                "the game's main thread did not respond in time (loading, or paused in the background: enable Run In Background)", "/help#main-thread"),
            KeyNotFoundException => ApiException.NotFound(ex.Message, "/help/objects#handles"),
            _ when exception is TargetInvocationException => new ApiException(400, "runtime_error", "the game code threw " + Values.Message(ex), "/help/objects"),
            FormatException or ArgumentException or InvalidOperationException or InvalidCastException or MissingMemberException
                or AmbiguousMatchException or TypeLoadException or NotSupportedException or OverflowException => ApiException.BadRequest(Values.Message(ex)),
            _ => null,
        };

        if (error == null)
        {
            Options.OnError?.Invoke(ex.ToString());
            error = new ApiException(500, "internal", Values.Message(ex));
        }

        if (details != null)
            error = new ApiException(error.Status, error.Code, error.Message, error.Help, details);
        return HttpResponse.Json(error.ToBody(), error.Status);
    }

    private bool IsAuthorized(HttpRequest request)
    {
        var header = request.Header("Authorization");
        const string scheme = "Bearer ";
        return header != null && header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
               && CryptographicOperations.FixedTimeEquals(Hash(header.Substring(scheme.Length).Trim()), _tokenHash);
    }

    // Digests have a fixed length, so comparing them leaks nothing about the token.
    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
