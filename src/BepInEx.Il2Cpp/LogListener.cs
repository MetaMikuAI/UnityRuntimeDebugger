using BepInEx.Logging;
using UnityRuntimeDebugger.Logging;
using BepInExLevel = BepInEx.Logging.LogLevel;
using LogLevel = UnityRuntimeDebugger.Logging.LogLevel;

namespace UnityRuntimeDebugger.Il2Cpp;

/// <summary>Copies every BepInEx log event (Unity's own log included) into the debugger's buffer.</summary>
internal sealed class LogListener : ILogListener
{
    private readonly LogBuffer _buffer;

    public LogListener(LogBuffer buffer) => _buffer = buffer;

    public BepInExLevel LogLevelFilter => BepInExLevel.All;

    public void LogEvent(object sender, LogEventArgs eventArgs)
        => _buffer.Add(Level(eventArgs.Level), eventArgs.Source?.SourceName ?? "?", eventArgs.Data?.ToString() ?? "");

    public void Dispose()
    {
    }

    private static LogLevel Level(BepInExLevel level)
    {
        if ((level & BepInExLevel.Fatal) != 0) return LogLevel.Fatal;
        if ((level & BepInExLevel.Error) != 0) return LogLevel.Error;
        if ((level & BepInExLevel.Warning) != 0) return LogLevel.Warn;
        if ((level & BepInExLevel.Message) != 0) return LogLevel.Message;
        if ((level & BepInExLevel.Info) != 0) return LogLevel.Info;
        return LogLevel.Debug;
    }
}
