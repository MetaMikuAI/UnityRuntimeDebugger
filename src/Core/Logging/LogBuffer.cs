namespace UnityRuntimeDebugger.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Message,
    Warn,
    Error,
    Fatal,
}

public sealed record LogEntry(long Seq, long Time, LogLevel Level, string Source, string Text);

/// <summary>Recent log events of all sources (the engine's included); the loader adapter feeds it.</summary>
public sealed class LogBuffer
{
    public Feed<LogEntry> Feed { get; } = new(5000);

    public void Add(LogLevel level, string source, string text)
        => Feed.Add(seq => new LogEntry(seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), level, source, text));
}
