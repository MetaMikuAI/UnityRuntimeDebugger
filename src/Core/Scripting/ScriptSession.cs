using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using UnityRuntimeDebugger.Hosting;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Scripting;

/// <summary>Raw result of running code, before values are described for the client.</summary>
internal sealed record Outcome(object? Value, bool IsVoid, string Output, ImmutableArray<Diagnostic> Diagnostics, Exception? Exception)
{
    public bool Compiled => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public static Outcome NotCompiled(ImmutableArray<Diagnostic> diagnostics) => new(null, true, "", diagnostics, null);
}

/// <summary>
/// Interactive C# session (like csi): bare statements run directly, variables persist between
/// submissions, and a trailing expression without a semicolon is the result. Compiles on the
/// calling thread; only execution hops to the game's main thread.
/// </summary>
internal sealed class ScriptSession
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly MainThread _main;
    private readonly object _sync = new();
    private readonly Lazy<ScriptOptions> _options;
    private volatile ScriptState<object>? _state;

    public ScriptSession(MainThread main)
    {
        _main = main;
        _options = new Lazy<ScriptOptions>(() => ScriptOptions.Default
            .WithReferences(References.All())
            .WithImports(References.Imports())
            .WithAllowUnsafe(true));
    }

    public ScriptOptions Options => _options.Value;

    public Outcome Submit(string code)
    {
        lock (_sync)
        {
            var previous = _state;
            // Continuing the same Script chain is required: a fresh CSharpScript cannot resume a state.
            var script = previous == null ? CSharpScript.Create(code, Options) : previous.Script.ContinueWith(code);
            var diagnostics = script.Compile();
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return Outcome.NotCompiled(diagnostics);

            var output = new StringWriter();
            var state = _main.Run(() => ConsoleCapture.Run(output, () =>
            {
                // Keep the session when a submission throws: earlier variables stay usable.
                var run = previous == null
                    ? script.RunAsync(null, _ => true)
                    : script.RunFromAsync(previous, _ => true);
                return run.GetAwaiter().GetResult();
            }), Timeout);

            _state = state;
            return new Outcome(state.ReturnValue, !ReturnsValue(script), output.ToString(), diagnostics, state.Exception);
        }
    }

    /// <summary>
    /// Whether the submission ends in a non-void expression without a semicolon, which tells
    /// "returned null" apart from "returned nothing".
    /// </summary>
    private static bool ReturnsValue(Script script)
    {
        var compilation = script.GetCompilation();
        var tree = compilation.SyntaxTrees.Last();
        if (tree.GetRoot() is not CompilationUnitSyntax unit
            || unit.Members.LastOrDefault() is not GlobalStatementSyntax { Statement: ExpressionStatementSyntax { SemicolonToken.IsMissing: true } statement })
            return false;

        // A null literal has no type of its own but is still a value.
        var type = compilation.GetSemanticModel(tree).GetTypeInfo(statement.Expression).Type;
        return type?.SpecialType != SpecialType.System_Void;
    }

    /// <summary>A not-yet-run continuation, for semantic analysis (completion). Safe off the main thread.</summary>
    public Script ContinueWith(string code)
    {
        var state = _state;
        return state == null ? CSharpScript.Create(code, Options) : state.Script.ContinueWith(code);
    }

    /// <summary>Session variables, the latest declaration of each name.</summary>
    public IReadOnlyList<ScriptVariable> Variables()
    {
        var state = _state;
        if (state == null)
            return Array.Empty<ScriptVariable>();

        var latest = new Dictionary<string, ScriptVariable>(StringComparer.Ordinal);
        foreach (var variable in state.Variables)
            latest[variable.Name] = variable;
        return latest.Values.ToList();
    }

    public void Reset()
    {
        lock (_sync)
            _state = null;
    }
}

internal static class ConsoleCapture
{
    /// <summary>Runs with <c>Console.Out</c> redirected; call on the thread that runs the code.</summary>
    public static T Run<T>(TextWriter output, Func<T> func)
    {
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            return func();
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
