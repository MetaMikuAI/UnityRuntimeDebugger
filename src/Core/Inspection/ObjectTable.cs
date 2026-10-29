using System.Runtime.CompilerServices;
using UnityRuntimeDebugger.Hosting;

namespace UnityRuntimeDebugger.Inspection;

/// <summary>
/// Handle table: keeps inspected objects alive so clients, and console code through <c>$n</c>, can
/// refer to them by a small integer. One object read twice gets one handle (the runtime decides
/// what "one object" means for interop wrappers). Bounded LRU; evicted handles stop resolving.
/// </summary>
public sealed class ObjectTable
{
    private const int Capacity = 20000;

    private readonly IRuntime _runtime;
    private readonly object _sync = new();
    private readonly Dictionary<int, LinkedListNode<Entry>> _byId = new();
    private readonly Dictionary<object, int> _byKey = new(KeyComparer.Instance);
    private readonly LinkedList<Entry> _lru = new();
    private int _next;

    public ObjectTable(IRuntime runtime) => _runtime = runtime;

    /// <summary>The last console result, <c>$_</c> in code.</summary>
    public object? Last { get; set; }

    public int Count
    {
        get
        {
            lock (_sync)
                return _byId.Count;
        }
    }

    public int Register(object value)
    {
        object? key;
        try
        {
            key = _runtime.IdentityOf(value);
        }
        catch
        {
            key = null;
        }

        lock (_sync)
        {
            if (key != null && _byKey.TryGetValue(key, out var existing) && _byId.TryGetValue(existing, out var found))
            {
                Touch(found);
                return existing;
            }

            var id = ++_next;
            _byId[id] = _lru.AddFirst(new Entry(id, value, key));
            if (key != null)
                _byKey[key] = id;

            while (_byId.Count > Capacity && _lru.Last != null)
                Remove(_lru.Last);
            return id;
        }
    }

    public bool TryGet(int id, out object value)
    {
        lock (_sync)
        {
            if (_byId.TryGetValue(id, out var node))
            {
                Touch(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }

    public object Get(int id)
        => TryGet(id, out var value) ? value : throw new KeyNotFoundException($"${id} has expired or never existed");

    public void Clear()
    {
        lock (_sync)
        {
            _byId.Clear();
            _byKey.Clear();
            _lru.Clear();
            Last = null;
        }
    }

    private void Touch(LinkedListNode<Entry> node)
    {
        if (node == _lru.First)
            return;
        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        _byId.Remove(node.Value.Id);
        if (node.Value.Key != null)
            _byKey.Remove(node.Value.Key);
    }

    private sealed record Entry(int Id, object Value, object? Key);

    /// <summary>Struct keys (native pointer + type) compare by value; objects that are their own key by reference.</summary>
    private sealed class KeyComparer : IEqualityComparer<object>
    {
        public static readonly KeyComparer Instance = new();

        public new bool Equals(object? x, object? y) => x is ValueType ? x.Equals(y) : ReferenceEquals(x, y);

        public int GetHashCode(object obj) => obj is ValueType ? obj.GetHashCode() : RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>The static side of a type, so types can be inspected like objects.</summary>
public sealed class StaticView
{
    private static readonly Dictionary<Type, StaticView> Cache = new();

    private StaticView(Type type) => Type = type;

    public Type Type { get; }

    public static StaticView Of(Type type)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(type, out var view))
                Cache[type] = view = new StaticView(type);
            return view;
        }
    }

    public override string ToString() => TypeNames.Display(Type);
}
