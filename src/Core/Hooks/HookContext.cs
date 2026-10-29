using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using UnityRuntimeDebugger.Hosting;
using UnityRuntimeDebugger.Inspection;
using UnityRuntimeDebugger.Logging;

namespace UnityRuntimeDebugger.Hooks;

/// <summary>A value captured at call time; <see cref="Target"/> gets a handle when a client reads the call.</summary>
internal readonly record struct Snapshot(ValueView View, object? Target);

internal sealed record CallRecord(
    long Seq,
    long Time,
    long Frame,
    int Thread,
    bool MainThread,
    Snapshot? Instance,
    Snapshot[] Args,
    Snapshot? Result,
    string? Exception,
    double DurationUs);

/// <summary>
/// What generated patches call into. <see cref="Enter"/> runs first in the prefix and decides
/// whether the call is ours; <see cref="Exit"/> and <see cref="Fail"/> record it. Nothing here may
/// throw into the game, and recording never re-enters hooks (describing a value can call a hooked
/// getter).
/// </summary>
public sealed class HookContext
{
    /// <summary>Recorded calls per second per hook; calls beyond it are only counted.</summary>
    private const int RecordsPerSecond = 200;

    private static readonly ConcurrentDictionary<int, HookContext> Contexts = new();

    [ThreadStatic]
    private static bool _recording;

    private readonly MainThread _main;
    private readonly IRuntime _runtime;
    private readonly Values _values;
    private readonly Type? _filterType;
    private long _hits, _recorded, _dropped, _foreign;
    private readonly object _rate = new();
    private long _window;
    private int _windowCount;

    internal HookContext(int id, MethodBase method, MainThread main, IRuntime runtime, Values values, bool filterByType)
    {
        Id = id;
        _main = main;
        _runtime = runtime;
        _values = values;
        _filterType = filterByType && !method.IsStatic ? method.DeclaringType : null;
        ArgNames = method.GetParameters().Select(p => p.Name ?? "arg" + p.Position).ToArray();
    }

    public int Id { get; }

    internal string[] ArgNames { get; }

    internal Feed<CallRecord> Calls { get; } = new(500);

    /// <summary>Calls of the hooked method (after the ICF filter).</summary>
    internal long Hits => Interlocked.Read(ref _hits);

    internal long Recorded => Interlocked.Read(ref _recorded);

    /// <summary>Calls over the recording rate limit.</summary>
    internal long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Calls that reached the hooked function through a folded method and were let through.</summary>
    internal long Foreign => Interlocked.Read(ref _foreign);

    public static HookContext Get(int id) => Contexts.TryGetValue(id, out var context) ? context : throw new InvalidOperationException($"hook {id} is not registered");

    internal static void Register(HookContext context) => Contexts[context.Id] = context;

    internal static void Unregister(int id) => Contexts.TryRemove(id, out _);

    /// <summary>
    /// -1: not ours (a folded method, or a call made while recording): run the original untouched.
    /// 0: ours but not recorded. Otherwise a timestamp to pass to <see cref="Exit"/>.
    /// </summary>
    public long Enter(object? instance)
    {
        if (_recording)
            return -1;

        try
        {
            if (_filterType != null && instance != null && !_runtime.IsInstanceOf(instance, _filterType))
            {
                Interlocked.Increment(ref _foreign);
                return -1;
            }

            Interlocked.Increment(ref _hits);
            if (!TakeRecordSlot())
            {
                Interlocked.Increment(ref _dropped);
                return 0;
            }

            return Math.Max(1, Stopwatch.GetTimestamp());
        }
        catch
        {
            return 0;
        }
    }

    public void Exit(long token, object? instance, object?[] args, object? result, bool hasResult)
        => Record(token, instance, args, result, hasResult, null);

    public Exception? Fail(long token, Exception? exception)
    {
        if (exception != null && token > 0)
            Record(token, null, Array.Empty<object?>(), null, false, Values.Message(exception));
        return exception;
    }

    private void Record(long token, object? instance, object?[] args, object? result, bool hasResult, string? exception)
    {
        if (token <= 0 || _recording)
            return;

        _recording = true;
        try
        {
            var duration = (Stopwatch.GetTimestamp() - token) * 1_000_000.0 / Stopwatch.Frequency;
            var onMain = _main.IsCurrent;
            var record = new CallRecord(
                0,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _main.Frame,
                Environment.CurrentManagedThreadId,
                onMain,
                instance == null ? null : Snap(instance, onMain),
                args.Select(a => Snap(a, onMain)).ToArray(),
                hasResult ? Snap(result, onMain) : null,
                exception,
                Math.Round(duration, 1));
            Calls.Add(seq => record with { Seq = seq });
            Interlocked.Increment(ref _recorded);
        }
        catch
        {
            // never break the game for a log line
        }
        finally
        {
            _recording = false;
        }
    }

    /// <summary>Off the main thread only cheap facts are read: engine objects refuse other threads.</summary>
    private Snapshot Snap(object? value, bool onMain)
    {
        if (onMain)
            return new Snapshot(_values.Snapshot(value, out var target), target);

        return value switch
        {
            null => new Snapshot(ValueView.Null, null),
            _ when value is string or bool or char or Enum || value.GetType().IsPrimitive => new Snapshot(_values.Snapshot(value, out var target), target),
            _ => new Snapshot(new ValueView { Kind = ValueKind.Object, Type = TypeNames.Display(value.GetType()), Text = "(captured off the main thread)" }, value),
        };
    }

    private bool TakeRecordSlot()
    {
        var second = Environment.TickCount64 / 1000;
        lock (_rate)
        {
            if (second != _window)
            {
                _window = second;
                _windowCount = 0;
            }

            return ++_windowCount <= RecordsPerSecond;
        }
    }
}
