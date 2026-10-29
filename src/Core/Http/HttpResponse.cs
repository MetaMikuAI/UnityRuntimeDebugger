using System.IO.Compression;
using System.Text;

namespace UnityRuntimeDebugger.Http;

public sealed class HttpResponse
{
    private HttpResponse(int status, string contentType, byte[] body)
    {
        Status = status;
        ContentType = contentType;
        Body = body;
    }

    public int Status { get; }

    public string ContentType { get; }

    public byte[] Body { get; }

    /// <summary>Pre-compressed body, sent instead of <see cref="Body"/> to clients that accept gzip.</summary>
    public byte[]? Gzip { get; private init; }

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static HttpResponse Json(object? value, int status = 200)
        => new(status, "application/json; charset=utf-8", Http.Json.Serialize(value));

    public static HttpResponse Text(string text, string contentType = "text/plain; charset=utf-8", int status = 200)
        => new(status, contentType, Encoding.UTF8.GetBytes(text));

    public static HttpResponse Bytes(byte[] body, string contentType, int status = 200) => new(status, contentType, body);

    /// <summary>A response that is served many times (static files): compressed once up front.</summary>
    public static HttpResponse Cached(byte[] body, string contentType)
        => new(200, contentType, body) { Gzip = Compressible(contentType) ? Compress(body) : null };

    public HttpResponse WithHeader(string name, string value)
    {
        Headers[name] = value;
        return this;
    }

    internal async Task WriteAsync(Stream stream, HttpRequest? request, CancellationToken cancellation)
    {
        var gzip = Gzip != null && request?.Header("Accept-Encoding")?.Contains("gzip", StringComparison.OrdinalIgnoreCase) == true;
        var body = gzip ? Gzip! : Body;

        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(Status).Append(' ').Append(Reason(Status)).Append("\r\n")
            .Append("Content-Type: ").Append(ContentType).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Connection: close\r\n");
        if (gzip)
            head.Append("Content-Encoding: gzip\r\n");
        if (!Headers.ContainsKey("Cache-Control"))
            head.Append("Cache-Control: no-store\r\n");
        foreach (var (name, value) in Headers)
            head.Append(name).Append(": ").Append(value).Append("\r\n");
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), cancellation);
        if (request?.Method != "HEAD")
            await stream.WriteAsync(body, cancellation);
        await stream.FlushAsync(cancellation);
    }

    private static bool Compressible(string contentType)
        => contentType.StartsWith("text/", StringComparison.Ordinal) || contentType.Contains("javascript") || contentType.Contains("json")
           || contentType.Contains("svg");

    private static byte[] Compress(byte[] body)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(body);
        return output.ToArray();
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        410 => "Gone",
        413 => "Payload Too Large",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "Unknown",
    };
}
