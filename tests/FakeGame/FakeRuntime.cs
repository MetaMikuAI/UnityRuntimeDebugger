#nullable enable
using System.Reflection;
using UnityRuntimeDebugger.Hosting;

namespace FakeGame;

/// <summary>
/// Managed objects, plus a stand-in for IL2CPP's identical code folding: methods of the game
/// assembly whose IL is byte-identical (and tiny) are reported as sharing one native function,
/// so the hook UI's ICF warning and the folded-hook API can be exercised without a real game.
/// </summary>
internal sealed class FakeRuntime : ManagedRuntime
{
    private const int MaxFoldedIl = 8;

    private readonly Lazy<Dictionary<string, List<MethodInfo>>> _groups = new(() => typeof(FakeRuntime).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(m => !m.IsAbstract && !m.ContainsGenericParameters)
        .Select(m => (method: m, key: Key(m)))
        .Where(x => x.key != null)
        .GroupBy(x => x.key!)
        .ToDictionary(g => g.Key, g => g.Select(x => x.method).ToList()));

    public override NativeMethod? NativeMethodOf(MethodBase method)
    {
        if (method.DeclaringType?.Assembly != typeof(FakeRuntime).Assembly || method is not MethodInfo info)
            return null;

        var key = Key(info);
        var group = key != null && _groups.Value.TryGetValue(key, out var list) ? list : new List<MethodInfo> { info };
        var address = group.Min(m => m.MethodHandle.Value.ToInt64());
        var folded = group.Where(m => m != info).Select(m => $"{m.DeclaringType!.FullName}::{m.Name}").ToList();
        return new NativeMethod(address, $"Assembly-CSharp.dll+0x{address & 0xFFFFFF:X}", folded);
    }

    private static string? Key(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null || il.Length > MaxFoldedIl)
            return null;
        var shape = string.Join(",", method.GetParameters().Select(p => p.ParameterType.IsValueType ? p.ParameterType.Name : "ref"));
        return $"{method.IsStatic}:{method.ReturnType.Name}({shape}):{Convert.ToHexString(il)}";
    }
}
