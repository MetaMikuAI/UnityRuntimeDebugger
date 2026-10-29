using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using UnityRuntimeDebugger.Hosting;

namespace UnityRuntimeDebugger.Il2Cpp;

/// <summary>
/// Compiled code behind interop methods. IL2CPP builds are linked with identical code folding
/// (MSVC /OPT:ICF, lld --icf), so methods with byte-identical bodies (trivial getters, empty
/// methods, shared generic code) end up at one address, and a detour on one of them catches
/// calls to all. The address index over every method in the domain is built once, on first use.
/// </summary>
internal static class NativeMethods
{
    private const int MaxFoldedNames = 30;
    private const uint AbstractFlag = 0x0400; // METHOD_ATTRIBUTE_ABSTRACT

    private static readonly object Sync = new();
    private static Dictionary<IntPtr, List<IntPtr>>? _index;

    public static NativeMethod? Of(MethodBase method)
    {
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
        if (field == null)
            return null; // a managed method (mods, injected classes)

        // Interface and abstract methods point at shared stubs, not code of their own.
        var methodInfo = (IntPtr)field.GetValue(null)!;
        var address = methodInfo == IntPtr.Zero || IsAbstract(methodInfo) ? IntPtr.Zero : Marshal.ReadIntPtr(methodInfo); // MethodInfo starts with methodPointer
        if (address == IntPtr.Zero)
            return new NativeMethod(0, null, null);

        var folded = Index().TryGetValue(address, out var all) ? all.Where(m => m != methodInfo).ToList() : new List<IntPtr>();
        var names = folded.Take(MaxFoldedNames).Select(Name).ToList();
        if (folded.Count > MaxFoldedNames)
            names.Add($"... and {folded.Count - MaxFoldedNames} more");
        return new NativeMethod(address.ToInt64(), Modules.Locate(address), names);
    }

    private static Dictionary<IntPtr, List<IntPtr>> Index()
    {
        lock (Sync)
        {
            if (_index != null)
                return _index;

            // A thread of our own, attached to the Il2Cpp domain for the walk and detached after,
            // so neither the game's main thread nor a pool thread is affected.
            Dictionary<IntPtr, List<IntPtr>>? index = null;
            Exception? error = null;
            var worker = new Thread(() =>
            {
                var thread = IL2CPP.il2cpp_thread_attach(IL2CPP.il2cpp_domain_get());
                try
                {
                    index = Build();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    IL2CPP.il2cpp_thread_detach(thread);
                }
            }) { IsBackground = true, Name = "UnityRuntimeDebugger ICF index" };
            worker.Start();
            worker.Join();

            return _index = index ?? throw new InvalidOperationException("building the native method index failed", error);
        }
    }

    private static unsafe Dictionary<IntPtr, List<IntPtr>> Build()
    {
        var index = new Dictionary<IntPtr, List<IntPtr>>();
        uint count = 0;
        var assemblies = IL2CPP.il2cpp_domain_get_assemblies(IL2CPP.il2cpp_domain_get(), ref count);
        for (var a = 0; a < count; a++)
        {
            var image = IL2CPP.il2cpp_assembly_get_image(assemblies[a]);
            var classes = IL2CPP.il2cpp_image_get_class_count(image);
            for (uint c = 0; c < classes; c++)
            {
                var klass = IL2CPP.il2cpp_image_get_class(image, c);
                var iterator = IntPtr.Zero;
                IntPtr method;
                while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iterator)) != IntPtr.Zero)
                {
                    var address = Marshal.ReadIntPtr(method);
                    if (address == IntPtr.Zero || IsAbstract(method))
                        continue;
                    if (!index.TryGetValue(address, out var list))
                        index[address] = list = new List<IntPtr>(1);
                    list.Add(method);
                }
            }
        }

        return index;
    }

    private static bool IsAbstract(IntPtr method)
    {
        uint implementationFlags = 0;
        return (IL2CPP.il2cpp_method_get_flags(method, ref implementationFlags) & AbstractFlag) != 0;
    }

    /// <summary><c>Namespace.Outer+Inner::Method</c> of a native MethodInfo.</summary>
    private static string Name(IntPtr method)
    {
        var klass = IL2CPP.il2cpp_method_get_class(method);
        var chain = new List<string>();
        var outer = klass;
        for (var k = klass; k != IntPtr.Zero; k = IL2CPP.il2cpp_class_get_declaring_type(k))
        {
            chain.Insert(0, IL2CPP.il2cpp_class_get_name_(k) ?? "?");
            outer = k;
        }

        var ns = IL2CPP.il2cpp_class_get_namespace_(outer);
        var type = string.Join("+", chain);
        return (string.IsNullOrEmpty(ns) ? type : ns + "." + type) + "::" + IL2CPP.il2cpp_method_get_name_(method);
    }
}

/// <summary>Turns an address into "module+offset", for looking it up in a disassembler.</summary>
internal static class Modules
{
    public static string? Locate(IntPtr address)
    {
        try
        {
            return OperatingSystem.IsWindows() ? Windows(address) : Posix(address);
        }
        catch
        {
            return null;
        }
    }

    private static string? Windows(IntPtr address)
    {
        const uint fromAddress = 0x4, unchangedRefCount = 0x2;
        if (!GetModuleHandleExW(fromAddress | unchangedRefCount, address, out var module))
            return null;

        var path = new char[1024];
        var length = GetModuleFileNameW(module, path, (uint)path.Length);
        return length == 0 ? null : $"{Path.GetFileName(new string(path, 0, (int)length))}+0x{address.ToInt64() - module.ToInt64():X}";
    }

    private static string? Posix(IntPtr address)
    {
        // Bionic exports dladdr from libdl; glibc 2.34+ from libc. CoreCLR on Android may not report IsAndroid.
        DlInfo info;
        int found;
        try
        {
            found = dladdr_libdl(address, out info);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            found = dladdr_libc(address, out info);
        }

        if (found == 0 || info.Base == IntPtr.Zero)
            return null;
        var file = Marshal.PtrToStringAnsi(info.File);
        return $"{Path.GetFileName(file)}+0x{address.ToInt64() - info.Base.ToInt64():X}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DlInfo
    {
        public IntPtr File;
        public IntPtr Base;
        public IntPtr Symbol;
        public IntPtr SymbolAddress;
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetModuleHandleExW(uint flags, IntPtr address, out IntPtr module);

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileNameW(IntPtr module, char[] path, uint size);

    [DllImport("libdl.so", EntryPoint = "dladdr")]
    private static extern int dladdr_libdl(IntPtr address, out DlInfo info);

    [DllImport("libc", EntryPoint = "dladdr")]
    private static extern int dladdr_libc(IntPtr address, out DlInfo info);
}
