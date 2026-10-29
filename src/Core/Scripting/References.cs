using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Scripting;

/// <summary>
/// Metadata references for compiling against everything the game has loaded. Rebuilt only when
/// the set of loaded assembly files changes. Assemblies without a file (loaded from bytes) cannot
/// be referenced; the loader adapters compile the debugger itself into a file-backed plugin DLL.
/// </summary>
internal static class References
{
    // Framework assemblies scripts commonly need; touching them makes sure they are loaded.
    private static readonly Type[] Baseline =
    {
        typeof(Console), typeof(Enumerable), typeof(HashSet<>), typeof(System.Text.RegularExpressions.Regex),
        typeof(System.Diagnostics.Stopwatch), typeof(System.Collections.Concurrent.ConcurrentDictionary<,>),
        typeof(HarmonyLib.Harmony),
    };

    private static readonly string[] DefaultImports =
    {
        "System", "System.Linq", "System.Collections.Generic", "System.Text", "System.Threading.Tasks", "UnityEngine",
    };

    private static readonly object Sync = new();
    private static List<MetadataReference>? _cached;
    private static HashSet<string>? _locations;

    public static List<MetadataReference> All()
    {
        GC.KeepAlive(Baseline);
        var locations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        lock (Sync)
        {
            if (_cached == null || _locations == null || !_locations.SetEquals(locations))
            {
                _cached = new List<MetadataReference>(locations.Count);
                foreach (var location in locations)
                {
                    try
                    {
                        _cached.Add(MetadataReference.CreateFromFile(location));
                    }
                    catch
                    {
                        // unreadable metadata: skip the assembly
                    }
                }

                _locations = new HashSet<string>(locations, StringComparer.Ordinal);
            }

            return new List<MetadataReference>(_cached);
        }
    }

    /// <summary>
    /// Namespaces every snippet, program and hook source can use without a using. Ones nobody defines
    /// (UnityEngine outside a game) are left out, or every compilation would fail.
    /// </summary>
    public static IEnumerable<string> Imports() => DefaultImports.Where(TypeCatalog.HasNamespace);

    /// <summary><see cref="Imports"/> as a <c>global using</c> file for full compilations.</summary>
    public static SyntaxTree GlobalUsings()
        => CSharpSyntaxTree.ParseText(string.Concat(Imports().Select(ns => $"global using {ns};\n")), path: "GlobalUsings.cs");
}
