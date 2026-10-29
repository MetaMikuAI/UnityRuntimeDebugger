using System.Text.Json;
using UnityRuntimeDebugger.Http;
using UnityRuntimeDebugger.Scripting;

namespace UnityRuntimeDebugger.Api;

internal static class EvalApi
{
    private sealed record EvalRequest(string? Code, EvalMode Mode = EvalMode.Script);

    private sealed record CompleteRequest(string Code, int Position);

    private sealed record WatchRequest(List<string>? Expressions);

    public static void Map(Router router)
    {
        router.Group("Code");

        router.Post("/api/eval", "Run C#. Script mode keeps variables between calls and returns a trailing expression; program mode runs a full program's Execute().", async c =>
            {
                var (code, mode) = Parse(await c.Text());
                return c.Debugger.Scripts.Eval(code, mode);
            })
            .Body("{\"code\": \"Time.frameCount\"}", "`code`; `mode`: `script` (default) or `program`. A plain-text body is script code.")
            .Note("Check `ok`; `error.code` is `compile_error` or `runtime_error`.");

        router.Post("/api/eval/complete", "Completions at a position in script code (the session's variables are known).", async c =>
            {
                var body = await c.Body<CompleteRequest>();
                return c.Debugger.Scripts.Complete(body.Code, body.Position);
            })
            .Body("{\"code\": \"Time.\", \"position\": 5}");

        router.Get("/api/eval/vars", "Variables declared in the script session.", c => c.Debugger.Scripts.Variables());

        router.Delete("/api/eval/session", "Reset the script session (forget variables and usings).", c =>
        {
            c.Debugger.Scripts.Reset();
            return new { ok = true };
        });

        router.Post("/api/watch", "Evaluate several expressions at once (compiled once, cached). They see `$n` handles, not session variables.", async c =>
            {
                var body = await c.Body<WatchRequest>();
                return c.Debugger.Scripts.Watch(body.Expressions ?? new List<string>());
            })
            .Body("{\"expressions\": [\"Time.frameCount\", \"$12.position\"]}");
    }

    /// <summary>JSON <c>{"code": ...}</c>, or the raw body as script code (curl -d, piped files).</summary>
    private static (string code, EvalMode mode) Parse(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                if (JsonSerializer.Deserialize<EvalRequest>(trimmed, Json.Options) is { Code: { } code } request)
                    return (code, request.Mode);
            }
            catch (JsonException)
            {
                // a C# block, not JSON
            }
        }

        return string.IsNullOrWhiteSpace(body) ? throw ApiException.BadRequest("no code: send {\"code\": \"...\"} or the code as the body", "/help/eval") : (body, EvalMode.Script);
    }
}
