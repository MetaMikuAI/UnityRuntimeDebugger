using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityRuntimeDebugger.Hosting;
using Object = UnityEngine.Object;

namespace UnityRuntimeDebugger.Il2Cpp;

/// <summary>Pumps the main-thread queue from an injected MonoBehaviour's Update.</summary>
public class MainThreadPump : MonoBehaviour
{
    private static MainThread? _queue;

    public MainThreadPump(IntPtr pointer) : base(pointer)
    {
    }

    internal static void Install(MainThread queue)
    {
        _queue = queue;
        ClassInjector.RegisterTypeInIl2Cpp<MainThreadPump>();
        var host = Interop.New<GameObject>("UnityRuntimeDebugger");
        Object.DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent(Il2CppType.Of<MainThreadPump>());
    }

    private void Update() => _queue?.Pump();
}

/// <summary>
/// Pumps the queue from a Dobby hook on <c>EventSystem.Update</c>, for loaders where an injected
/// MonoBehaviour never gets Update (seen on some Android setups). Needs an EventSystem in the scene.
/// </summary>
internal static class NativeUpdateHook
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void UpdateFn(IntPtr instance, IntPtr method);

    private static readonly UpdateFn Hook = Update;
    private static UpdateFn? _original;
    private static IntPtr _target;
    private static MainThread? _queue;

    public static void Install(MainThread queue)
    {
        var klass = IL2CPP.GetIl2CppClass("UnityEngine.UI.dll", "UnityEngine.EventSystems", "EventSystem");
        var method = klass == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_class_get_method_from_name(klass, "Update", 0);
        if (method == IntPtr.Zero)
            throw new InvalidOperationException("EventSystem.Update not found");

        var target = Marshal.ReadIntPtr(method);
        var status = DobbyHook(target, Marshal.GetFunctionPointerForDelegate(Hook), out var original);
        if (status != 0)
            throw new InvalidOperationException($"DobbyHook(EventSystem.Update) returned {status}");

        _queue = queue;
        _target = target;
        _original = Marshal.GetDelegateForFunctionPointer<UpdateFn>(original);
    }

    public static void Uninstall()
    {
        if (_target != IntPtr.Zero && DobbyDestroy(_target) == 0)
            _target = IntPtr.Zero;
    }

    private static void Update(IntPtr instance, IntPtr method)
    {
        _original!(instance, method);
        _queue?.Pump();
    }

    [DllImport("dobby")]
    private static extern int DobbyHook(IntPtr address, IntPtr replacement, out IntPtr original);

    [DllImport("dobby")]
    private static extern int DobbyDestroy(IntPtr address);
}
