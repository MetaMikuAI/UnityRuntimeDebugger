using System.Reflection;

namespace UnityRuntimeDebugger.Hosting;

/// <summary>
/// How the scripting backend exposes game objects to managed code. Mono needs nothing special
/// (<see cref="ManagedRuntime"/>); IL2CPP interop wrappers need unboxing, re-wrapping as their
/// runtime class, pointer identity and native method addresses.
/// </summary>
public interface IRuntime
{
    /// <summary>Shown in the status, e.g. "IL2CPP" or "Mono".</summary>
    string Name { get; }

    /// <summary>
    /// The value as it should be shown: a wrapper of its most derived runtime type, boxed
    /// primitives and structs unboxed, native strings as managed strings.
    /// </summary>
    object? Normalize(object value);

    /// <summary>
    /// Key that identifies the underlying object, so one native object read twice gets one handle.
    /// Null for values that are never deduplicated (boxed structs).
    /// </summary>
    object? IdentityOf(object value);

    /// <summary>True when the native side was garbage collected under a live wrapper.</summary>
    bool IsCollected(object value);

    string? AddressOf(object value);

    /// <summary>Types whose members live on the native side (shown with a marker).</summary>
    bool IsNativeType(Type type);

    /// <summary>Base types that add nothing worth listing (object, Il2CppSystem.Object...).</summary>
    bool IsRootType(Type type);

    /// <summary>A property that stands for a native field (interop generators emit fields as properties).</summary>
    bool IsNativeField(PropertyInfo property);

    /// <summary>
    /// Text for a native object: true when the runtime handled the value, with null text when its
    /// ToString would only echo the type name. False for plain managed values.
    /// </summary>
    bool TryToString(object value, out string? text);

    /// <summary>
    /// Converts a value to a parameter or field type it does not directly fit: the same native object
    /// under another wrapper type, a System.Type passed where the runtime's own Type is expected...
    /// </summary>
    bool TryAdapt(object value, Type target, out object? adapted);

    /// <summary>Native code behind a method, or null for managed methods.</summary>
    NativeMethod? NativeMethodOf(MethodBase method);

    /// <summary>
    /// Whether the object's runtime class derives from <paramref name="type"/>. Hooks use it to drop
    /// calls that reach a folded (ICF) function through another method.
    /// </summary>
    bool IsInstanceOf(object instance, Type type);
}

/// <param name="Address">Absolute address of the compiled function.</param>
/// <param name="Location">Module-relative location such as <c>GameAssembly.dll+0x1A2B30</c>, when known.</param>
/// <param name="FoldedWith">
/// Other methods compiled to the same function by identical code folding: a hook on any of them
/// fires for all of them. Null when the address index is not built yet.
/// </param>
public sealed record NativeMethod(long Address, string? Location, IReadOnlyList<string>? FoldedWith);
