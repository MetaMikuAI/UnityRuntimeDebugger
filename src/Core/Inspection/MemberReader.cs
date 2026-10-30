using System.Globalization;
using System.Reflection;
using System.Text.Json;
using UnityRuntimeDebugger.Hosting;

namespace UnityRuntimeDebugger.Inspection;

public enum MemberKind
{
    Field,
    Property,
    Constant,
}

/// <summary>Why a member is not read until asked for.</summary>
public enum LazyReason
{
    /// <summary>Static getters such as singleton <c>Instance</c> accessors may create what they return.</summary>
    Static,

    /// <summary>The getter changes state (instantiates a material copy, advances the RNG...).</summary>
    SideEffect,
}

public sealed class MemberView
{
    public string Name { get; init; } = "";
    public MemberKind Kind { get; init; }

    /// <summary>A native field (exposed by the interop generator as a property).</summary>
    public bool? Native { get; init; }

    public string Type { get; init; } = "";
    public string DeclaringType { get; init; } = "";

    /// <summary>Inheritance distance from the inspected type; 0 is declared on the type itself.</summary>
    public int Depth { get; init; }

    public bool? Static { get; init; }
    public bool? Writable { get; set; }
    public Editor? Editor { get; set; }
    public string[]? Options { get; set; }
    public ValueView? Value { get; set; }
    public LazyReason? Lazy { get; set; }
    public string? Error { get; set; }
}

/// <summary>Reflection over the fields and properties of live objects and static types. Main thread only.</summary>
public sealed class MemberReader
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.DeclaredOnly;

    private static readonly HashSet<string> SideEffectGetters = new(StringComparer.Ordinal)
    {
        "UnityEngine.Renderer.material",
        "UnityEngine.Renderer.materials",
        "UnityEngine.MeshFilter.mesh",
        "UnityEngine.Collider.material",
        "UnityEngine.Collider2D.sharedMaterial",
        "UnityEngine.UI.Graphic.materialForRendering",
        "TMPro.TMP_Text.fontMaterial",
        "TMPro.TMP_Text.fontMaterials",
        "UnityEngine.Random.value",
        "UnityEngine.Random.insideUnitCircle",
        "UnityEngine.Random.insideUnitSphere",
        "UnityEngine.Random.onUnitSphere",
        "UnityEngine.Random.rotation",
        "UnityEngine.Random.rotationUniform",
    };

    /// <summary>Obsolete Unity shortcut accessors (<c>component.rigidbody</c>...) that always throw.</summary>
    private static readonly HashSet<string> ObsoleteShortcuts = new(StringComparer.Ordinal)
    {
        "rigidbody", "rigidbody2D", "camera", "light", "animation", "constantForce", "renderer", "audio",
        "networkView", "collider", "collider2D", "hingeJoint", "particleSystem", "guiText", "guiTexture", "guiElement",
    };

    private readonly IRuntime _runtime;
    private readonly Values _values;
    private readonly ValueParser _parser;

    public MemberReader(IRuntime runtime, Values values, ValueParser parser)
    {
        _runtime = runtime;
        _values = values;
        _parser = parser;
    }

    public static (Type type, object? instance) Target(object target)
        => target is StaticView view ? (view.Type, null) : (target.GetType(), target);

    /// <param name="readStatics">Also read static properties, which are lazy by default.</param>
    /// <param name="only">Read just this member, lazy or not.</param>
    public List<MemberView> Read(object target, bool readStatics = false, string? only = null)
    {
        var (type, instance) = Target(target);
        var statics = instance == null;
        if (statics && type.IsEnum)
            return EnumConstants(type);

        var members = new List<MemberView>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var flags = Declared | (statics ? BindingFlags.Static : BindingFlags.Instance);
        var boxedStruct = instance?.GetType().IsValueType ?? false;

        var depth = 0;
        for (var t = type; t != null && !_runtime.IsRootType(t); t = t.BaseType, depth++)
        {
            var declaring = TypeNames.Display(t);

            foreach (var field in Safe(() => t.GetFields(flags)))
            {
                if (only != null && field.Name != only || field.Name.Contains('<') || !seen.Add(field.Name))
                    continue;

                var view = new MemberView
                {
                    Name = field.Name,
                    Kind = field.IsLiteral ? MemberKind.Constant : MemberKind.Field,
                    Type = TypeNames.Display(field.FieldType),
                    DeclaringType = declaring,
                    Depth = depth,
                    Static = statics ? true : null,
                };
                SetEditor(view, field.FieldType, !field.IsLiteral && !field.IsInitOnly && !boxedStruct);
                ReadInto(view, () => field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(instance));
                members.Add(view);
            }

            foreach (var property in Safe(() => t.GetProperties(flags)))
            {
                if (only != null && property.Name != only)
                    continue;
                if (property.GetIndexParameters().Length > 0 || property.GetMethod?.IsPublic != true || IsObsoleteShortcut(t, property) || !seen.Add(property.Name))
                    continue;

                var native = _runtime.NativeFieldAttributes(property);
                var view = new MemberView
                {
                    Name = property.Name,
                    Kind = native == null ? MemberKind.Property
                        : (native.Value & FieldAttributes.Literal) != 0 ? MemberKind.Constant : MemberKind.Field,
                    Native = native != null ? true : null,
                    Type = TypeNames.Display(property.PropertyType),
                    DeclaringType = declaring,
                    Depth = depth,
                    Static = statics ? true : null,
                };
                SetEditor(view, property.PropertyType, CanWrite(property, native) && !boxedStruct);

                var sideEffect = SideEffectGetters.Contains(t.FullName + "." + property.Name);
                if (only == null && sideEffect)
                    view.Lazy = LazyReason.SideEffect;
                else if (only == null && statics && native == null && !readStatics)
                    view.Lazy = LazyReason.Static;
                else
                    ReadInto(view, () => property.GetValue(instance));

                members.Add(view);
            }
        }

        return members;
    }

    /// <summary>Reads one member, also one that is lazy by default.</summary>
    public MemberView ReadOne(object target, string name)
        => Read(target, only: name).FirstOrDefault() ?? throw new MissingMemberException($"no readable member named '{name}'");

    public MemberView Write(object target, string name, JsonElement value)
    {
        var (type, instance) = Target(target);
        var flags = BindingFlags.Public | (instance == null ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);

        if (type.GetField(name, flags) is { } field)
        {
            if (field.IsLiteral || field.IsInitOnly)
                throw new InvalidOperationException($"{name} is read-only");
            field.SetValue(instance, _parser.Convert(value, field.FieldType));
            return ReadOne(target, name);
        }

        var property = type.GetProperties(flags).FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0)
                       ?? throw new MissingMemberException($"no member named '{name}'");
        if (!CanWrite(property, _runtime.NativeFieldAttributes(property)))
            throw new InvalidOperationException($"{name} is read-only");

        property.SetValue(instance, _parser.Convert(value, property.PropertyType));
        return ReadOne(target, name);
    }

    private static bool CanWrite(PropertyInfo property, FieldAttributes? native)
        => property.SetMethod?.IsPublic == true
           && (native.GetValueOrDefault() & (FieldAttributes.Literal | FieldAttributes.InitOnly)) == 0;

    /// <summary>The type and its listed base types, most derived first.</summary>
    public List<string> Bases(Type type)
    {
        var chain = new List<string>();
        for (var t = type; t != null && !_runtime.IsRootType(t); t = t.BaseType)
            chain.Add(TypeNames.Display(t));
        return chain;
    }

    internal static void SetEditor(MemberView view, Type type, bool writable)
    {
        if (ValueParser.EditorFor(type, out var options) is not { } editor)
            return;
        view.Editor = editor;
        view.Options = options;
        view.Writable = writable ? true : null;
    }

    private void ReadInto(MemberView view, Func<object?> read)
    {
        try
        {
            view.Value = _values.Describe(read());
        }
        catch (Exception ex)
        {
            view.Error = Values.Message(ex);
        }
    }

    private List<MemberView> EnumConstants(Type type)
    {
        var underlying = Enum.GetUnderlyingType(type);
        return type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => new MemberView
            {
                Name = f.Name,
                Kind = MemberKind.Constant,
                Type = TypeNames.Display(underlying),
                DeclaringType = TypeNames.Display(type),
                Static = true,
                Value = _values.Describe(Convert.ChangeType(f.GetRawConstantValue(), underlying, CultureInfo.InvariantCulture)),
            })
            .ToList();
    }

    private static bool IsObsoleteShortcut(Type declaring, PropertyInfo property)
        => ObsoleteShortcuts.Contains(property.Name) && declaring.FullName is "UnityEngine.Component" or "UnityEngine.GameObject";

    private static T[] Safe<T>(Func<T[]> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return Array.Empty<T>();
        }
    }
}
