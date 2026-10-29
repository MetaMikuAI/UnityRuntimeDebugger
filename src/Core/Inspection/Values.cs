using System.Collections;
using System.Globalization;
using System.Reflection;
using UnityRuntimeDebugger.Hosting;

namespace UnityRuntimeDebugger.Inspection;

public enum ValueKind
{
    Null,
    String,
    Number,
    Bool,
    Char,
    Enum,
    Pointer,
    Type,
    Object,
    Struct,
    Destroyed,
    Error,
    Void,
}

/// <summary>Compact preview of a value. Anything that can be drilled into carries a handle.</summary>
public sealed class ValueView
{
    public ValueKind Kind { get; init; }

    public string? Type { get; init; }

    public string? Text { get; init; }

    public int? Handle { get; set; }

    /// <summary>Element count of collections.</summary>
    public int? Count { get; init; }

    /// <summary>Full length of strings; <see cref="Text"/> may be cut short.</summary>
    public int? Length { get; init; }

    public bool? Truncated { get; init; }

    /// <summary>RGBA in 0–1 for colors.</summary>
    public float[]? Color { get; init; }

    public static readonly ValueView Null = new() { Kind = ValueKind.Null };

    public static readonly ValueView Void = new() { Kind = ValueKind.Void };

    public static ValueView Error(string message, string? type = null) => new() { Kind = ValueKind.Error, Text = message, Type = type };
}

/// <summary>Turns live values into <see cref="ValueView"/>s. Reads engine objects: main thread only.</summary>
public sealed class Values
{
    public const int PreviewLength = 300;

    private static readonly Dictionary<Type, bool> CollectionCache = new();
    private static readonly Dictionary<Type, PropertyInfo?> CountCache = new();

    private readonly IRuntime _runtime;
    private readonly IGame _game;
    private readonly ObjectTable _objects;

    public Values(IRuntime runtime, IGame game, ObjectTable objects)
    {
        _runtime = runtime;
        _game = game;
        _objects = objects;
    }

    public ValueView Describe(object? value) => Describe(value, register: true, out _);

    /// <summary>
    /// Describes a value without registering a handle; <paramref name="target"/> is what a handle
    /// would point at, for callers (hook call records) that register lazily.
    /// </summary>
    public ValueView Snapshot(object? value, out object? target) => Describe(value, register: false, out target);

    private ValueView Describe(object? value, bool register, out object? target)
    {
        target = null;
        if (value == null)
            return ValueView.Null;
        if (_runtime.IsCollected(value))
            return ValueView.Error("collected by the native garbage collector", TypeNames.Display(value.GetType()));

        value = _runtime.Normalize(value);
        switch (value)
        {
            case null:
                return ValueView.Null;
            case string s:
                if (s.Length > PreviewLength)
                    target = s;
                return new ValueView
                {
                    Kind = ValueKind.String,
                    Text = s.Length > PreviewLength ? s.Substring(0, PreviewLength) : s,
                    Length = s.Length,
                    Truncated = s.Length > PreviewLength ? true : null,
                    Handle = Handle(target, register),
                };
            case bool b:
                return new ValueView { Kind = ValueKind.Bool, Text = b ? "true" : "false" };
            case char c:
                return new ValueView { Kind = ValueKind.Char, Text = c.ToString() };
            case Enum e:
                return new ValueView { Kind = ValueKind.Enum, Type = TypeNames.Display(e.GetType()), Text = e.ToString() };
            case IntPtr p:
                return new ValueView { Kind = ValueKind.Pointer, Type = "IntPtr", Text = "0x" + p.ToString("X") };
            case UIntPtr p:
                return new ValueView { Kind = ValueKind.Pointer, Type = "UIntPtr", Text = "0x" + p.ToUInt64().ToString("X") };
            case Type t:
                target = StaticView.Of(t);
                return new ValueView { Kind = ValueKind.Type, Type = "Type", Text = TypeNames.Display(t), Handle = Handle(target, register) };
            case StaticView view:
                target = view;
                return new ValueView { Kind = ValueKind.Type, Type = "Type", Text = TypeNames.Display(view.Type), Handle = Handle(target, register) };
        }

        var type = value.GetType();
        var typeName = TypeNames.Display(type);
        if (type.IsPrimitive || value is decimal)
            return new ValueView { Kind = ValueKind.Number, Type = typeName, Text = Convert.ToString(value, CultureInfo.InvariantCulture) };

        target = value;
        if (type.IsValueType)
        {
            return new ValueView
            {
                Kind = ValueKind.Struct,
                Type = typeName,
                Text = VectorText(value, type) ?? SafeToString(value, type),
                Color = ColorOf(value, type),
                Handle = Handle(target, register),
            };
        }

        if (_game.IsDestroyed(value))
        {
            target = null;
            return new ValueView { Kind = ValueKind.Destroyed, Type = typeName, Text = "destroyed" };
        }

        string? text;
        int? count = null;
        if (TryName(value, out var name))
            text = name;
        else if (_runtime.TryToString(value, out var native))
            (text, count) = (Truncate(native), CountOf(value));
        else
            (text, count) = (value is Delegate d ? d.Method.Name : SafeToString(value, type), CountOf(value));

        return new ValueView { Kind = ValueKind.Object, Type = typeName, Text = text, Count = count, Handle = Handle(target, register) };
    }

    private int? Handle(object? target, bool register) => register && target != null ? _objects.Register(target) : null;

    private bool TryName(object value, out string? name)
    {
        try
        {
            name = _game.NameOf(value);
            return name != null;
        }
        catch
        {
            // some native objects refuse name lookups
            name = null;
            return false;
        }
    }

    /// <summary>
    /// Collections expose items. Managed enumerables qualify, as do native types with a parameterless
    /// GetEnumerator (Il2Cpp List, Dictionary, HashSet, Transform...).
    /// </summary>
    public bool IsCollection(Type type)
    {
        if (type == typeof(string) || type == typeof(StaticView))
            return false;

        lock (CollectionCache)
        {
            if (CollectionCache.TryGetValue(type, out var cached))
                return cached;
        }

        var result = typeof(IEnumerable).IsAssignableFrom(type);
        if (!result && _runtime.IsNativeType(type) && !type.FullName!.EndsWith(".String", StringComparison.Ordinal))
            result = type.GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) != null;

        lock (CollectionCache)
            CollectionCache[type] = result;
        return result;
    }

    public int? CountOf(object value)
    {
        try
        {
            switch (value)
            {
                case string:
                    return null;
                case Array array:
                    return array.Length;
                case ICollection collection:
                    return collection.Count;
            }

            var type = value.GetType();
            return IsCollection(type) && CountProperty(type)?.GetValue(value) is int count ? count : null;
        }
        catch
        {
            return null;
        }
    }

    public static PropertyInfo? CountProperty(Type type)
    {
        lock (CountCache)
        {
            if (!CountCache.TryGetValue(type, out var property))
                CountCache[type] = property = IntProperty(type, "Count") ?? IntProperty(type, "Length");
            return property;
        }
    }

    public static string? SafeToString(object value, Type type)
    {
        try
        {
            var method = type.GetMethod("ToString", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            if (method == null || method.DeclaringType == typeof(object) || method.DeclaringType == typeof(ValueType))
                return null;
            return Truncate(value.ToString());
        }
        catch (Exception ex)
        {
            return "<ToString threw " + Unwrap(ex).GetType().Name + ">";
        }
    }

    public static string Truncate(string? text, int max = PreviewLength)
        => text == null ? "" : text.Length <= max ? text : text.Substring(0, max) + "…";

    public static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: { } inner })
            ex = inner;
        return ex;
    }

    /// <summary>"Type: first line of the message", native stack traces stripped.</summary>
    public static string Message(Exception ex)
    {
        ex = Unwrap(ex);
        var text = ex.Message;
        var newline = text.IndexOf('\n');
        if (newline > 0)
            text = text.Substring(0, newline).TrimEnd('\r');
        return ex.GetType().Name + ": " + Truncate(text, 400);
    }

    /// <summary>
    /// Full-precision "(x, y, z)" for vector-like structs; Unity's own ToString rounds to two
    /// decimals, which would also make inline edits lossy.
    /// </summary>
    private static string? VectorText(object value, Type type)
    {
        var fields = ValueParser.IsColor(type) ? type.GetFields(BindingFlags.Public | BindingFlags.Instance) : ValueParser.VectorFields(type);
        if (fields == null)
            return null;

        try
        {
            return "(" + string.Join(", ", fields.Select(f => Convert.ToString(f.GetValue(value), CultureInfo.InvariantCulture))) + ")";
        }
        catch
        {
            return null;
        }
    }

    private static float[]? ColorOf(object value, Type type)
    {
        if (!ValueParser.IsColor(type))
            return null;

        try
        {
            var scale = ValueParser.IsColor32(type) ? 255f : 1f;
            return new[] { "r", "g", "b", "a" }
                .Select(n => Convert.ToSingle(type.GetField(n)!.GetValue(value), CultureInfo.InvariantCulture) / scale)
                .ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static PropertyInfo? IntProperty(Type type, string name)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.Name == name && p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0);
}
