namespace UnityRuntimeDebugger.Logging;

/// <param name="Items">Oldest first.</param>
/// <param name="Cursor">Pass back as <c>after</c> to get only what comes next.</param>
/// <param name="Dropped">Entries between the requested cursor and the first item fell out of the buffer.</param>
public sealed record FeedPage<T>(IReadOnlyList<T> Items, long Cursor, bool Dropped);

/// <summary>
/// Bounded, sequence-numbered buffer that clients tail with a cursor, optionally waiting
/// (long-polling) for new entries. Logs and hook calls are both feeds.
/// </summary>
public sealed class Feed<T>
{
    private readonly T[] _ring;
    private readonly object _sync = new();
    private long _next;
    private long _start;
    private TaskCompletionSource<bool> _signal = NewSignal();

    public Feed(int capacity) => _ring = new T[capacity];

    public long Count
    {
        get
        {
            lock (_sync)
                return _next;
        }
    }

    /// <summary>Appends an entry built from its sequence number.</summary>
    public void Add(Func<long, T> create)
    {
        TaskCompletionSource<bool> signal;
        lock (_sync)
        {
            _ring[_next % _ring.Length] = create(_next);
            _next++;
            signal = _signal;
            _signal = NewSignal();
        }

        signal.TrySetResult(true);
    }

    /// <summary>
    /// Entries after <paramref name="after"/> (-1: from the start), oldest first. Without a cursor
    /// the newest <paramref name="limit"/> entries are returned.
    /// </summary>
    public FeedPage<T> Since(long after, int limit)
    {
        lock (_sync)
        {
            var oldest = Math.Max(_start, _next - _ring.Length);
            var from = after < 0 ? Math.Max(oldest, _next - limit) : Math.Max(after + 1, oldest);
            var dropped = after < 0 ? from > _start : after + 1 < oldest;
            var items = new List<T>();
            for (var seq = from; seq < _next && items.Count < limit; seq++)
                items.Add(_ring[seq % _ring.Length]);
            return new FeedPage<T>(items, from + items.Count - 1, dropped);
        }
    }

    /// <summary>Like <see cref="Since"/>, but waits up to <paramref name="wait"/> for something new.</summary>
    public async Task<FeedPage<T>> WaitAsync(long after, int limit, TimeSpan wait, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            Task signal;
            lock (_sync)
                signal = _signal.Task;

            var page = Since(after, limit);
            var remaining = deadline - DateTime.UtcNow;
            if (page.Items.Count > 0 || remaining <= TimeSpan.Zero)
                return page;

            await Task.WhenAny(signal, Task.Delay(remaining, cancellation));
            cancellation.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Forgets the buffered entries; sequence numbers keep counting so cursors stay valid.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            Array.Clear(_ring);
            _start = _next;
        }
    }

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
