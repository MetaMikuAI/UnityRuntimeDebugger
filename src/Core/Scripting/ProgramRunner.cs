using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnityRuntimeDebugger.Hosting;

namespace UnityRuntimeDebugger.Scripting;

/// <summary>
/// Program mode: a complete compilation unit (usings, namespaces, types) whose
/// <c>public static object Execute()</c> (or <c>void Execute()</c>) runs once on the main thread.
/// Each program loads into its own collectible context and is unloaded afterwards, so state that
/// must outlive it (Harmony patches, event handlers) belongs in the console session or a hook.
/// </summary>
internal sealed class ProgramRunner
{
    private readonly MainThread _main;
    private int _count;

    public ProgramRunner(MainThread main) => _main = main;

    public Outcome Run(string code)
    {
        var compilation = CSharpCompilation.Create(
            $"urdbg-program-{Interlocked.Increment(ref _count)}",
            new[] { References.GlobalUsings(), CSharpSyntaxTree.ParseText(code) },
            References.All(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, optimizationLevel: OptimizationLevel.Debug));

        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if (!emit.Success)
            return Outcome.NotCompiled(emit.Diagnostics);

        image.Position = 0;
        var context = CreateContext();
        try
        {
            var assembly = context?.LoadFromStream(image) ?? Assembly.Load(image.ToArray());
            var entry = FindEntry(assembly);
            var output = new StringWriter();
            try
            {
                var value = _main.Run(() => ConsoleCapture.Run(output, () => entry.Invoke(null, null)), ScriptSession.Timeout);
                return new Outcome(value, entry.ReturnType == typeof(void), output.ToString(), emit.Diagnostics, null);
            }
            catch (TargetInvocationException ex)
            {
                return new Outcome(null, true, output.ToString(), emit.Diagnostics, ex.InnerException ?? ex);
            }
        }
        finally
        {
            // Completes only once nothing references the program anymore.
            context?.Unload();
        }
    }

    private static MethodInfo FindEntry(Assembly assembly)
    {
        var entries = assembly.GetTypes()
            .Select(t => t.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes))
            .Where(m => m != null)
            .ToList();

        return entries.Count switch
        {
            1 => entries[0]!,
            0 => throw new MissingMethodException("program mode needs an entry point: public static object Execute() in a public class"),
            _ => throw new AmbiguousMatchException("more than one public static Execute() in the program"),
        };
    }

    /// <summary>Collectible contexts are not available on every runtime; fall back to the default one.</summary>
    private static AssemblyLoadContext? CreateContext()
    {
        try
        {
            return new AssemblyLoadContext(null, isCollectible: true);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }
}
