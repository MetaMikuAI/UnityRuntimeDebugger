using Microsoft.CodeAnalysis;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Scripting;

public sealed class EvalResult
{
    public bool Ok { get; init; }

    /// <summary>The value of a trailing expression (script mode) or of <c>Execute()</c> (program mode).</summary>
    public ValueView? Value { get; set; }

    /// <summary>Everything written to <c>Console.Out</c> while the code ran.</summary>
    public string? Output { get; init; }

    public EvalError? Error { get; init; }

    public long DurationMs { get; set; }
}

/// <param name="Code"><c>compile_error</c> or <c>runtime_error</c>.</param>
/// <param name="Detail">Full exception text for runtime errors.</param>
public sealed record EvalError(string Code, string Message, IReadOnlyList<DiagnosticView>? Diagnostics = null, string? Detail = null);

/// <summary>A compiler message with 1-based line/column in the code as the client sent it.</summary>
public sealed record DiagnosticView(string Id, string Severity, string Message, int Line, int Column, int EndLine, int EndColumn)
{
    public override string ToString() => $"({Line},{Column}): {Severity} {Id}: {Message}";

    public static List<DiagnosticView> From(IEnumerable<Diagnostic> diagnostics, string original, List<HandleRewriter.Shift> shifts)
        => diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && d.Location.IsInSource && d.Location.SourceTree?.FilePath != "GlobalUsings.cs")
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id is not ("CS1701" or "CS1702"))
            .OrderByDescending(d => d.Severity)
            .Take(50)
            .Select(d =>
            {
                var span = d.Location.SourceSpan;
                var (line, column) = Position(original, HandleRewriter.MapBack(span.Start, shifts));
                var (endLine, endColumn) = Position(original, HandleRewriter.MapBack(span.End, shifts));
                return new DiagnosticView(d.Id, d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.GetMessage(), line, column, endLine, endColumn);
            })
            .ToList();

    private static (int line, int column) Position(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, offset - lineStart + 1);
    }
}
