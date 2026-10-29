using System.Collections;
using System.Reflection;

namespace UnityRuntimeDebugger.Inspection;

public sealed record ItemView(int Index, ValueView? Key, ValueView Value);

public sealed record ItemPage(int? Count, int Offset, List<ItemView> Items, bool More);

/// <summary>Pages through arrays, lists, dictionaries and native enumerables. Main thread only.</summary>
public sealed class CollectionReader
{
    private readonly Values _values;

    public CollectionReader(Values values) => _values = values;

    public ItemPage Read(object target, int offset, int limit)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 1000);
        var count = _values.CountOf(target);
        var items = new List<ItemView>();

        switch (target)
        {
            case Array { Rank: 1 } array:
                return Indexed(array.Length, i => array.GetValue(i));
            case IDictionary dictionary:
                return Enumerate(dictionary.GetEnumerator());
            case IList list:
                return Indexed(list.Count, i => list[i]);
        }

        // Il2Cpp arrays and List<T> have an int indexer: random access keeps paging cheap.
        var type = target.GetType();
        if (IntIndexer(type) is { } indexer && count is { } length)
            return Indexed(length, i => indexer.GetValue(target, new object[] { i }));

        if (target is IEnumerable enumerable)
            return Enumerate(enumerable.GetEnumerator());

        var getEnumerator = type.GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)
                            ?? throw new InvalidOperationException($"{TypeNames.Display(type)} is not a collection");
        var enumerator = getEnumerator.Invoke(target, null) ?? throw new InvalidOperationException("GetEnumerator returned null");
        return Enumerate(new ReflectionEnumerator(enumerator));

        ItemPage Indexed(int length, Func<int, object?> read)
        {
            for (var i = offset; i < length && i < offset + limit; i++)
            {
                var index = i;
                items.Add(Item(index, null, () => read(index)));
            }

            return new ItemPage(count ?? length, offset, items, offset + limit < length);
        }

        ItemPage Enumerate(IEnumerator enumerator)
        {
            try
            {
                var i = 0;
                while (i < offset && enumerator.MoveNext())
                    i++;
                while (i < offset + limit && enumerator.MoveNext())
                    items.Add(Entry(i++, enumerator.Current));
                return new ItemPage(count, offset, items, i == offset + limit && enumerator.MoveNext());
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }
    }

    private ItemView Entry(int index, object? current)
    {
        if (current is DictionaryEntry entry)
            return new ItemView(index, _values.Describe(entry.Key), _values.Describe(entry.Value));

        // Managed KeyValuePair<,> and the Il2Cpp wrapper share these member names.
        var type = current?.GetType();
        if (type != null && type.Name.StartsWith("KeyValuePair`2", StringComparison.Ordinal)
                         && type.GetProperty("Key") is { } key && type.GetProperty("Value") is { } value)
        {
            try
            {
                return new ItemView(index, _values.Describe(key.GetValue(current)), _values.Describe(value.GetValue(current)));
            }
            catch (Exception ex)
            {
                return new ItemView(index, null, ValueView.Error(Values.Message(ex)));
            }
        }

        return new ItemView(index, null, _values.Describe(current));
    }

    private ItemView Item(int index, ValueView? key, Func<object?> read)
    {
        try
        {
            return new ItemView(index, key, _values.Describe(read()));
        }
        catch (Exception ex)
        {
            return new ItemView(index, key, ValueView.Error(Values.Message(ex)));
        }
    }

    /// <summary><c>this[int]</c> of list-like types; dictionaries keyed by int are excluded.</summary>
    private static PropertyInfo? IntIndexer(Type type)
    {
        if (type.GetMethod("ContainsKey", BindingFlags.Public | BindingFlags.Instance) != null)
            return null;

        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(p =>
        {
            var parameters = p.GetIndexParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(int) && p.GetMethod?.IsPublic == true;
        });
    }

    /// <summary>Drives a native enumerator (MoveNext / Current) through reflection.</summary>
    private sealed class ReflectionEnumerator : IEnumerator
    {
        private readonly object _inner;
        private readonly MethodInfo _moveNext;
        private readonly PropertyInfo _current;

        public ReflectionEnumerator(object inner)
        {
            _inner = inner;
            var type = inner.GetType();
            _moveNext = type.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)
                        ?? throw new InvalidOperationException("enumerator has no MoveNext");
            _current = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                           .FirstOrDefault(p => p.Name == "Current" && p.GetIndexParameters().Length == 0)
                       ?? throw new InvalidOperationException("enumerator has no Current");
        }

        public object? Current => _current.GetValue(_inner);

        public bool MoveNext() => (bool)_moveNext.Invoke(_inner, null)!;

        public void Reset() => throw new NotSupportedException();
    }
}
