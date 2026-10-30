using System.Reflection;

namespace UnityRuntimeDebugger.Hosting;

/// <summary>Plain managed objects (Mono games, tests): nothing to unwrap.</summary>
public class ManagedRuntime : IRuntime
{
    public virtual string Name => "Mono";

    public virtual object? Normalize(object value) => value;

    public virtual object? IdentityOf(object value) => value.GetType().IsValueType ? null : value;

    public virtual bool IsCollected(object value) => false;

    public virtual string? AddressOf(object value) => null;

    public virtual bool IsNativeType(Type type) => false;

    public virtual bool IsRootType(Type type) => type == typeof(object) || type == typeof(ValueType) || type == typeof(Enum);

    public virtual FieldAttributes? NativeFieldAttributes(PropertyInfo property) => null;

    public virtual bool TryToString(object value, out string? text)
    {
        text = null;
        return false;
    }

    public virtual bool TryAdapt(object value, Type target, out object? adapted)
    {
        adapted = null;
        return false;
    }

    public virtual NativeMethod? NativeMethodOf(MethodBase method) => null;

    public virtual bool IsInstanceOf(object instance, Type type) => type.IsInstanceOfType(instance);
}
