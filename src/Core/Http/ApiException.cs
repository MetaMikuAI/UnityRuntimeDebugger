namespace UnityRuntimeDebugger.Http;

/// <summary>
/// An error the client can act on. Serialized as <c>{"error": {"code", "message", "help"}}</c>;
/// <see cref="Help"/> points at the /help page that explains the fix.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(int status, string code, string message, string? help = null, object? details = null) : base(message)
    {
        Status = status;
        Code = code;
        Help = help;
        Details = details;
    }

    public int Status { get; }

    public string Code { get; }

    public string? Help { get; }

    /// <summary>Structured extras, e.g. compiler diagnostics or the methods a hook would fold with.</summary>
    public object? Details { get; }

    public static ApiException BadRequest(string message, string? help = null) => new(400, "bad_request", message, help);

    public static ApiException NotFound(string message, string? help = null) => new(404, "not_found", message, help);

    public static ApiException Conflict(string message, string? help = null) => new(409, "conflict", message, help);

    public object ToBody() => new { error = new { code = Code, message = Message, help = Help, details = Details } };
}
