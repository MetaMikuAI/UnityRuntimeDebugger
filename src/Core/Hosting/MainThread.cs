using System.Collections.Concurrent;

namespace UnityRuntimeDebugger.Hosting;

/// <summary>
/// Work queue drained by the game once per frame. The loader adapter calls <see cref="Pump"/> from
/// an Update hook; everything that touches engine objects goes through <see cref="Run{T}"/>.
/// </summary>
public sealed class MainThread
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private Thread? _thread;
    private long _frame;

    /// <summary>Frames pumped so far; hooks stamp calls with it without touching engine APIs.</summary>
    public long Frame => Interlocked.Read(ref _frame);

    public bool IsCurrent => _thread == Thread.CurrentThread;

    /// <summary>Raised for exceptions thrown by posted actions.</summary>
    public event Action<Exception>? Error;

    public void Pump()
    {
        _thread ??= Thread.CurrentThread;
        Interlocked.Increment(ref _frame);
        while (_queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
            }
        }
    }

    public void Post(Action action) => _queue.Enqueue(action);

    /// <summary>
    /// Runs <paramref name="func"/> on the main thread and waits for it; exceptions are rethrown here.
    /// Throws <see cref="TimeoutException"/> when the game does not get to it in time (loading,
    /// paused in the background); work that has not started by then is dropped, not run late.
    /// </summary>
    public T Run<T>(Func<T> func, TimeSpan timeout)
    {
        if (IsCurrent)
            return func();

        var task = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = 0; // 0 queued, 1 running, 2 abandoned
        _queue.Enqueue(() =>
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
                return;
            try
            {
                task.TrySetResult(func());
            }
            catch (Exception ex)
            {
                task.TrySetException(ex);
            }
        });

        var done = ((IAsyncResult)task.Task).AsyncWaitHandle.WaitOne(timeout);
        if (!done && Interlocked.CompareExchange(ref state, 2, 0) == 0)
            throw new TimeoutException("the game's main thread did not respond");

        return task.Task.GetAwaiter().GetResult();
    }

    public void Run(Action action, TimeSpan timeout) => Run(() =>
    {
        action();
        return true;
    }, timeout);
}
