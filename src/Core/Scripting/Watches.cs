using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;

using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Scripting;

/// <summary>
/// Live watch expressions. Each distinct expression is compiled once into a standalone
/// delegate (not chained onto the console session, so polling never grows session
/// state) and re-run cheaply on every refresh. They can use statics and <c>$n</c>
/// handles, but not console variables.
/// </summary>
internal sealed class Watches
{
    private const int MaxCached = 256;

    private readonly object _sync = new();
    private readonly Dictionary<string, Compiled> _cache = new(StringComparer.Ordinal);
    private readonly ScriptSession _session;
    private readonly HandleRewriter _rewriter;

    public Watches(ScriptSession session, HandleRewriter rewriter)
    {
        _session = session;
        _rewriter = rewriter;
    }

    public sealed class Compiled
    {
        public Func<object?>? Run { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>Compiles (or fetches) an expression. Compiler-only; call off the main thread.</summary>
    public Compiled Compile(string expression)
    {
        var code = _rewriter.Rewrite(expression);
        lock (_sync)
        {
            if (_cache.TryGetValue(code, out var cached))
                return cached;
        }

        Compiled compiled;
        try
        {
            var script = CSharpScript.Create<object>(code, _session.Options);
            var errors = script.Compile().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                compiled = new Compiled { Error = string.Join("\n", errors.Select(e => e.GetMessage())) };
            }
            else
            {
                var runner = script.CreateDelegate();
                compiled = new Compiled { Run = () => runner().GetAwaiter().GetResult() };
            }
        }
        catch (Exception ex)
        {
            compiled = new Compiled { Error = Values.Message(ex) };
        }

        lock (_sync)
        {
            if (_cache.Count >= MaxCached)
                _cache.Clear();
            _cache[code] = compiled;
        }

        return compiled;
    }
}
