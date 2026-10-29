namespace UnityRuntimeDebugger.Hosting;

/// <param name="Token">Secret every /api request must present.</param>
/// <param name="Game">Engine access for this backend.</param>
/// <param name="Loader">The mod loader hosting the debugger, for the status view.</param>
public sealed record DebuggerOptions(string Token, IGame Game, LoaderInfo Loader)
{
    public string ListenAddress { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 18765;

    public IRuntime Runtime { get; init; } = new ManagedRuntime();

    /// <summary>The loader's log file, served by /api/logs/file.</summary>
    public string? LogFile { get; init; }

    /// <summary>Errors the debugger hit while serving requests; the loader routes them to its log.</summary>
    public Action<string>? OnError { get; init; }
}

public sealed record LoaderInfo(string Name, string Version, Func<IReadOnlyList<PluginInfo>> Plugins);

public sealed record PluginInfo(string Id, string Name, string Version);
