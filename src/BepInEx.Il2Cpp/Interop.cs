namespace UnityRuntimeDebugger.Il2Cpp;

internal static class Interop
{
    /// <summary>
    /// Calls a constructor that the reference assemblies the plugin builds against leave out; every
    /// game's interop assemblies have it.
    /// </summary>
    public static T New<T>(params object[] args) => (T)Activator.CreateInstance(typeof(T), args)!;
}
