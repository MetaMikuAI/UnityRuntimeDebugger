namespace UnityRuntimeDebugger;

/// <summary>
/// Handles from console code: <c>$12</c> compiles to <c>Handles.Get(12)</c> (cast to the object's
/// type) and <c>$_</c> to <see cref="Last"/>. <see cref="Of"/> hands out a handle for any value.
/// </summary>
public static class Handles
{
    public static object Get(int handle) => Current.Objects.Get(handle);

    public static object? Last => Current.Objects.Last;

    /// <summary>Registers a value and returns its handle, e.g. to open it in the inspector.</summary>
    public static int Of(object value) => Current.Objects.Register(value);

    private static Debugger Current => Debugger.Current ?? throw new InvalidOperationException("the debugger is not running");
}
