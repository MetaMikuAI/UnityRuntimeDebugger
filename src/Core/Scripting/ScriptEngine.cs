using System.Diagnostics;
using System.Reflection;
using UnityRuntimeDebugger.Hosting;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Scripting;

public enum EvalMode
{
    Script,
    Program,
}

public sealed record VariableView(string Name, string Type, ValueView Value);

public sealed record WatchView(ValueView? Value, string? Error);

/// <summary>Everything that runs C# for clients: the console session, programs, watches and completion.</summary>
internal sealed class ScriptEngine
{
    private static readonly TimeSpan DescribeTimeout = TimeSpan.FromSeconds(15);

    private readonly MainThread _main;
    private readonly ObjectTable _objects;
    private readonly Values _values;
    private readonly HandleRewriter _rewriter;
    private readonly ScriptSession _session;
    private readonly ProgramRunner _programs;
    private readonly Watches _watches;

    public ScriptEngine(MainThread main, ObjectTable objects, Values values)
    {
        _main = main;
        _objects = objects;
        _values = values;
        _rewriter = new HandleRewriter(objects);
        _session = new ScriptSession(main);
        _programs = new ProgramRunner(main);
        _watches = new Watches(_session, _rewriter);
    }

    public EvalResult Eval(string code, EvalMode mode)
    {
        var stopwatch = Stopwatch.StartNew();
        var rewritten = _rewriter.Rewrite(code, out var shifts);

        Outcome outcome;
        try
        {
            outcome = mode == EvalMode.Program ? _programs.Run(rewritten) : _session.Submit(rewritten);
        }
        catch (Exception ex) when (ex is MissingMethodException or AmbiguousMatchException)
        {
            return Failed("compile_error", ex.Message);
        }

        if (!outcome.Compiled)
        {
            var diagnostics = DiagnosticView.From(outcome.Diagnostics, code, shifts);
            var errors = diagnostics.Where(d => d.Severity == "error").ToList();
            return Failed("compile_error", errors.Count == 1 ? errors[0].ToString() : $"{errors.Count} compile errors; first: {errors.FirstOrDefault()}", diagnostics);
        }

        var output = outcome.Output.Length > 0 ? outcome.Output : null;
        if (outcome.Exception is { } exception)
        {
            return new EvalResult
            {
                Ok = false,
                Output = output,
                Error = new EvalError("runtime_error", Values.Message(exception), Detail: Values.Truncate(exception.ToString(), 6000)),
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }

        ValueView? value = null;
        if (!outcome.IsVoid)
        {
            if (outcome.Value != null)
                _objects.Last = outcome.Value;
            value = _main.Run(() => _values.Describe(outcome.Value), DescribeTimeout);
        }

        return new EvalResult { Ok = true, Value = value, Output = output, DurationMs = stopwatch.ElapsedMilliseconds };

        EvalResult Failed(string kind, string message, IReadOnlyList<DiagnosticView>? diagnostics = null) => new()
        {
            Ok = false,
            Error = new EvalError(kind, message, diagnostics),
            DurationMs = stopwatch.ElapsedMilliseconds,
        };
    }

    public Completion.Result Complete(string code, int position) => Completion.Complete(_session, _rewriter, code, position);

    /// <summary>Compiles on the calling thread, evaluates all expressions in one main-thread hop.</summary>
    public List<WatchView> Watch(IReadOnlyList<string> expressions)
    {
        var compiled = expressions.Select(_watches.Compile).ToList();
        return _main.Run(() => compiled.Select(c =>
        {
            if (c.Run == null)
                return new WatchView(null, c.Error);
            try
            {
                return new WatchView(_values.Describe(c.Run()), null);
            }
            catch (Exception ex)
            {
                return new WatchView(null, Values.Message(ex));
            }
        }).ToList(), DescribeTimeout);
    }

    public List<VariableView> Variables()
        => _main.Run(() => _session.Variables()
            .Select(v => new VariableView(v.Name, TypeNames.Display(v.Type), _values.Describe(v.Value)))
            .ToList(), DescribeTimeout);

    public void Reset() => _session.Reset();
}
