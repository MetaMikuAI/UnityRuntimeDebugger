using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using UnityRuntimeDebugger.Hosting;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Il2Cpp;

/// <summary>
/// Il2CppInterop's view of game objects: wrappers are re-created for every read and typed by the
/// declared member type, so values are re-wrapped as their runtime class, boxed natives unboxed,
/// and identity goes by native pointer.
/// </summary>
internal sealed class Il2CppRuntime : ManagedRuntime
{
    private static readonly Dictionary<IntPtr, Type?> ClassToType = new();
    private static readonly Dictionary<PropertyInfo, FieldAttributes?> NativeFields = new();
    private static readonly MethodInfo UnboxMethod = typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.Unbox))!;
    private static readonly MethodInfo PoolGet = typeof(Il2CppObjectPool).GetMethod(nameof(Il2CppObjectPool.Get))!;

    private static readonly Dictionary<string, Type> Primitives = new()
    {
        ["System.Boolean"] = typeof(bool),
        ["System.Char"] = typeof(char),
        ["System.SByte"] = typeof(sbyte),
        ["System.Byte"] = typeof(byte),
        ["System.Int16"] = typeof(short),
        ["System.UInt16"] = typeof(ushort),
        ["System.Int32"] = typeof(int),
        ["System.UInt32"] = typeof(uint),
        ["System.Int64"] = typeof(long),
        ["System.UInt64"] = typeof(ulong),
        ["System.Single"] = typeof(float),
        ["System.Double"] = typeof(double),
        ["System.IntPtr"] = typeof(IntPtr),
        ["System.UIntPtr"] = typeof(UIntPtr),
        ["System.String"] = typeof(string),
        ["System.Void"] = typeof(void),
    };

    public override string Name => "IL2CPP";

    public override object? Normalize(object value)
    {
        if (value is not Il2CppObjectBase obj || obj.WasCollected)
            return value;

        var pointer = obj.Pointer;
        if (pointer == IntPtr.Zero)
            return null;

        var runtimeType = Resolve(IL2CPP.il2cpp_object_get_class(pointer), pointer);
        if (runtimeType == null || runtimeType == value.GetType())
            return value;
        if (runtimeType == typeof(string))
            return IL2CPP.Il2CppStringToManaged(pointer);

        try
        {
            // Boxed primitives, enums and blittable structs come back as plain managed values.
            if (runtimeType.IsValueType)
                return UnboxMethod.MakeGenericMethod(runtimeType).Invoke(obj, null);
            return typeof(Il2CppObjectBase).IsAssignableFrom(runtimeType) && !runtimeType.IsAbstract ? Wrap(runtimeType, pointer) : value;
        }
        catch
        {
            return value;
        }
    }

    public override object? IdentityOf(object value) => value switch
    {
        _ when value.GetType().IsValueType => null,
        Il2CppObjectBase native => new NativeKey(native.Pointer, value.GetType()),
        _ => value,
    };

    public override bool IsCollected(object value) => value is Il2CppObjectBase { WasCollected: true };

    public override string? AddressOf(object value) => value is Il2CppObjectBase native ? "0x" + native.Pointer.ToString("X") : null;

    public override bool IsNativeType(Type type) => typeof(Il2CppObjectBase).IsAssignableFrom(type);

    public override bool IsRootType(Type type)
        => base.IsRootType(type) || type == typeof(Il2CppObjectBase) || type.FullName is "Il2CppSystem.Object" or "Il2CppSystem.ValueType" or "Il2CppSystem.Enum";

    /// <summary>The interop generator emits a private static <c>NativeFieldInfoPtr_&lt;name&gt;</c> next to each field property.</summary>
    public override FieldAttributes? NativeFieldAttributes(PropertyInfo property)
    {
        lock (NativeFields)
        {
            if (NativeFields.TryGetValue(property, out var attributes))
                return attributes;

            var field = property.DeclaringType?.GetField("NativeFieldInfoPtr_" + property.Name,
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                var pointer = (IntPtr)field.GetValue(null)!;
                if (pointer == IntPtr.Zero)
                    throw new InvalidOperationException($"native field not found: {property.DeclaringType?.FullName}.{property.Name}");
                attributes = (FieldAttributes)IL2CPP.il2cpp_field_get_flags(pointer);
            }

            return NativeFields[property] = attributes;
        }
    }

    /// <summary>The Il2Cpp ToString, or null when it only echoes the type name.</summary>
    public override bool TryToString(object value, out string? text)
    {
        text = null;
        if (value is not Il2CppObjectBase native)
            return false;
        if (value is Il2CppSystem.Object o)
        {
            try
            {
                text = o.ToString();
                if (string.IsNullOrEmpty(text) || text == NativeTypeName(native))
                    text = null;
            }
            catch (Exception ex)
            {
                text = "<ToString threw " + Values.Unwrap(ex).GetType().Name + ">";
            }
        }

        return true;
    }

    public override bool TryAdapt(object value, Type target, out object? adapted)
    {
        adapted = value switch
        {
            // Same native object under another wrapper type.
            Il2CppObjectBase native when IsNativeType(target) && !target.IsAbstract => Wrap(target, native.Pointer),
            Type type when target == typeof(Il2CppSystem.Type) => Il2CppType.From(type),
            _ => null,
        };
        return adapted != null;
    }

    public override NativeMethod? NativeMethodOf(MethodBase method) => NativeMethods.Of(method);

    public override bool IsInstanceOf(object instance, Type type)
    {
        if (instance is not Il2CppObjectBase native || !IsNativeType(type))
            return base.IsInstanceOf(instance, type);

        var expected = Il2CppClassPointerStore.GetNativeClassPointer(type);
        return expected == IntPtr.Zero || IL2CPP.il2cpp_class_is_assignable_from(expected, IL2CPP.il2cpp_object_get_class(native.Pointer));
    }

    /// <summary>
    /// Wrapper of <paramref name="type"/> for a native object, through the object pool so injected
    /// classes resolve to their existing managed instance.
    /// </summary>
    private static object Wrap(Type type, IntPtr pointer)
    {
        try
        {
            return PoolGet.MakeGenericMethod(type).Invoke(null, new object[] { pointer })!;
        }
        catch
        {
            return Activator.CreateInstance(type, pointer)!;
        }
    }

    private static string? NativeTypeName(Il2CppObjectBase obj)
    {
        var klass = IL2CPP.il2cpp_object_get_class(obj.Pointer);
        var ns = IL2CPP.il2cpp_class_get_namespace_(klass);
        var name = IL2CPP.il2cpp_class_get_name_(klass);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static Type? Resolve(IntPtr klass, IntPtr pointer)
    {
        lock (ClassToType)
        {
            if (ClassToType.TryGetValue(klass, out var cached))
                return cached;
        }

        Type? resolved = null;
        try
        {
            resolved = ToManaged(new Il2CppSystem.Object(pointer).GetIl2CppType(), 0);
        }
        catch
        {
            // shown as the declared wrapper type
        }

        lock (ClassToType)
            ClassToType[klass] = resolved;
        return resolved;
    }

    /// <summary>Maps an Il2Cpp reflection type onto its interop (managed) type.</summary>
    private static Type? ToManaged(Il2CppSystem.Type type, int depth)
    {
        if (depth > 8)
            return null;

        if (type.IsArray)
        {
            var element = ToManaged(type.GetElementType(), depth + 1);
            if (element == null)
                return null;
            if (element == typeof(string))
                return typeof(Il2CppStringArray);
            return element.IsValueType ? typeof(Il2CppStructArray<>).MakeGenericType(element) : typeof(Il2CppReferenceArray<>).MakeGenericType(element);
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var definition = ToManaged(type.GetGenericTypeDefinition(), depth + 1);
            var args = type.GetGenericArguments().Select(a => ToManaged(a, depth + 1)).ToArray();
            if (definition == null || args.Any(a => a == null))
                return null;
            return definition.MakeGenericType(args.Select(a => a == typeof(object) ? typeof(Il2CppSystem.Object) : a!).ToArray());
        }

        var name = NestedName(type);
        if (Primitives.TryGetValue(name, out var primitive))
            return primitive;
        return name == "System.Object" ? typeof(Il2CppSystem.Object) : TypeCatalog.FindInteropType(name);
    }

    /// <summary>"Namespace.Outer+Inner" in Il2Cpp (un-prefixed) naming.</summary>
    private static string NestedName(Il2CppSystem.Type type)
    {
        var chain = new List<string>();
        var outer = type;
        for (var t = type; t != null; t = t.IsNested ? t.DeclaringType : null)
        {
            chain.Insert(0, t.Name);
            outer = t;
        }

        var joined = string.Join("+", chain);
        return string.IsNullOrEmpty(outer.Namespace) ? joined : outer.Namespace + "." + joined;
    }

    private readonly record struct NativeKey(IntPtr Pointer, Type Type);
}
