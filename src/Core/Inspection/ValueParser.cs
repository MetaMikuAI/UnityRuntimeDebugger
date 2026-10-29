using System.Globalization;
using System.Reflection;
using System.Text.Json;
using UnityRuntimeDebugger.Hosting;

namespace UnityRuntimeDebugger.Inspection;

/// <summary>Inline editor a member or parameter gets in the UI.</summary>
public enum Editor
{
    Bool,
    Number,
    String,
    Char,
    Enum,
    Flags,
    Color,
    Vector,
}

/// <summary>
/// Turns client input into values of a target type: JSON scalars, text such as <c>(1, 2, 3)</c> or
/// <c>#ff8800</c>, and handle references (<c>{"handle": 12}</c> or <c>"$12"</c>).
/// </summary>
public sealed class ValueParser
{
    private const int MaxEnumOptions = 400;

    private readonly IRuntime _runtime;
    private readonly ObjectTable _objects;

    public ValueParser(IRuntime runtime, ObjectTable objects)
    {
        _runtime = runtime;
        _objects = objects;
    }

    public static Editor? EditorFor(Type type, out string[]? options)
    {
        options = null;
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(bool))
            return Editor.Bool;
        if (type == typeof(string))
            return Editor.String;
        if (type == typeof(char))
            return Editor.Char;
        if (type.IsEnum)
        {
            var names = Enum.GetNames(type);
            if (type.IsDefined(typeof(FlagsAttribute), false) || names.Length > MaxEnumOptions)
                return Editor.Flags;
            options = names;
            return Editor.Enum;
        }

        if (IsNumeric(type))
            return Editor.Number;
        if (IsColor(type))
            return Editor.Color;
        if (VectorFields(type) is { } fields)
        {
            options = fields.Select(f => f.Name).ToArray();
            return Editor.Vector;
        }

        return null;
    }

    public object? Convert(JsonElement element, Type target)
    {
        if (target.IsByRef)
            target = target.GetElementType()!;

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => Default(target),
            JsonValueKind.Object when element.TryGetProperty("handle", out var handle) => FromHandle(handle.GetInt32(), target),
            JsonValueKind.True => Parse("true", target),
            JsonValueKind.False => Parse("false", target),
            JsonValueKind.Number => Parse(element.GetRawText(), target),
            JsonValueKind.String => Parse(element.GetString() ?? "", target),
            _ => throw new FormatException("unsupported value " + element.GetRawText()),
        };
    }

    public object? Parse(string text, Type target)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 1 && trimmed[0] == '$' && int.TryParse(trimmed.AsSpan(1), out var id))
            return FromHandle(id, target);

        if (Nullable.GetUnderlyingType(target) is { } underlying)
        {
            if (trimmed.Length == 0 || trimmed == "null")
                return null;
            target = underlying;
        }

        if (target == typeof(string))
            return text;
        if (trimmed == "null" && !target.IsValueType)
            return null;
        if (target == typeof(bool))
            return trimmed switch { "1" => true, "0" => false, _ => bool.Parse(trimmed) };
        if (target == typeof(char))
            return text.Length == 1 ? text[0] : throw new FormatException("expected a single character");
        if (target.IsEnum)
            return Enum.Parse(target, trimmed, ignoreCase: true);
        if (IsNumeric(target))
            return ParseNumber(trimmed, target);
        if (IsColor(target))
            return ParseColor(trimmed, target);
        if (VectorFields(target) is { } fields)
            return ParseVector(trimmed, target, fields);

        // A type name, for System.Type or the runtime's own Type (Il2CppSystem.Type).
        if (target == typeof(Type) || target.Name == "Type")
        {
            var type = TypeCatalog.Find(trimmed) ?? throw new TypeLoadException("type not found: " + trimmed);
            if (target.IsInstanceOfType(type))
                return type;
            if (_runtime.TryAdapt(type, target, out var adapted))
                return adapted;
        }

        throw new FormatException($"cannot turn '{Values.Truncate(text, 60)}' into {TypeNames.Display(target)}; pass a handle such as $12");
    }

    private object? FromHandle(int id, Type target)
    {
        var value = _objects.Get(id);
        if (value is StaticView view)
            value = view.Type;
        if (target.IsInstanceOfType(value))
            return value;
        if (_runtime.TryAdapt(value, target, out var adapted))
            return adapted;
        throw new InvalidCastException($"${id} is {TypeNames.Display(value.GetType())}, expected {TypeNames.Display(target)}");
    }

    private static object? Default(Type target)
        => target.IsValueType && Nullable.GetUnderlyingType(target) == null ? Activator.CreateInstance(target) : null;

    /// <summary>Color / Color32 shaped structs: public r, g, b, a fields.</summary>
    public static bool IsColor(Type type)
        => type.IsValueType && type.Name is "Color" or "Color32"
           && new[] { "r", "g", "b", "a" }.All(n => type.GetField(n, BindingFlags.Public | BindingFlags.Instance) != null);

    public static bool IsColor32(Type type) => IsColor(type) && type.GetField("r")!.FieldType == typeof(byte);

    public static bool IsNumeric(Type type)
        => type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long)
           || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong)
           || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(decimal);

    /// <summary>Structs made only of 2–4 numeric fields (Vector2/3/4, Quaternion, Vector2Int, Rect...).</summary>
    public static FieldInfo[]? VectorFields(Type type)
    {
        if (!type.IsValueType || type.IsPrimitive || type.IsEnum)
            return null;

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        return fields.Length is >= 2 and <= 4 && fields.All(f => IsNumeric(f.FieldType)) ? fields : null;
    }

    private static object ParseNumber(string text, Type target)
    {
        if (target == typeof(float) || target == typeof(double) || target == typeof(decimal))
            return System.Convert.ChangeType(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture), target, CultureInfo.InvariantCulture);

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return System.Convert.ChangeType(ulong.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), target, CultureInfo.InvariantCulture);

        var number = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (number != decimal.Truncate(number))
            throw new FormatException($"{TypeNames.Display(target)} needs a whole number");
        return System.Convert.ChangeType(number, target, CultureInfo.InvariantCulture);
    }

    private object ParseVector(string text, Type target, FieldInfo[] fields)
    {
        var parts = text.Trim('(', ')', '[', ']', ' ').Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != fields.Length)
            throw new FormatException($"expected {fields.Length} numbers ({string.Join(", ", fields.Select(f => f.Name))})");

        var boxed = Activator.CreateInstance(target)!;
        for (var i = 0; i < fields.Length; i++)
            fields[i].SetValue(boxed, Parse(parts[i], fields[i].FieldType));
        return boxed;
    }

    /// <summary>Accepts <c>#RRGGBB</c>, <c>#RRGGBBAA</c> or <c>r, g, b[, a]</c> in 0–1 (Color) / 0–255 (Color32).</summary>
    private static object ParseColor(string text, Type target)
    {
        var bytes = IsColor32(target);
        float[] rgba;
        if (text.StartsWith('#'))
        {
            var hex = text.Substring(1);
            if (hex.Length is not (6 or 8))
                throw new FormatException("expected #RRGGBB or #RRGGBBAA");
            rgba = Enumerable.Range(0, 4)
                .Select(i => i * 2 < hex.Length ? int.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber) / 255f : 1f)
                .Select(v => bytes ? v * 255f : v)
                .ToArray();
        }
        else
        {
            var parts = text.Trim('(', ')', ' ').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 3 or > 4)
                throw new FormatException("expected r, g, b[, a]");
            rgba = parts.Select(p => float.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            if (rgba.Length == 3)
                rgba = rgba.Append(bytes ? 255f : 1f).ToArray();
        }

        var boxed = Activator.CreateInstance(target)!;
        var names = new[] { "r", "g", "b", "a" };
        for (var i = 0; i < 4; i++)
        {
            var field = target.GetField(names[i])!;
            var component = field.FieldType == typeof(byte) ? MathF.Round(rgba[i]) : rgba[i];
            field.SetValue(boxed, System.Convert.ChangeType(component, field.FieldType, CultureInfo.InvariantCulture));
        }

        return boxed;
    }
}
