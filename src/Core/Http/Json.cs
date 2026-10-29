using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityRuntimeDebugger.Http;

/// <summary>One JSON dialect for the whole API: camelCase, nulls omitted, enums as camelCase strings.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Quotes and CJK stay readable; responses are never HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static byte[] Serialize(object? value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static string Stringify(object? value) => JsonSerializer.Serialize(value, Options);
}
