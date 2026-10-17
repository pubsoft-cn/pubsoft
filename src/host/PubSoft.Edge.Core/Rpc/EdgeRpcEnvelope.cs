using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSoft.Edge;

/// <summary>
/// Edge credential scheme — how <see cref="EdgeRpcRequest.Credential"/> is interpreted.
///
/// Default <see cref="Hmac"/> (HMAC-SHA256). New integrations use Hmac; <see cref="Md5"/>
/// exists for clients that cannot move off MD5 signing (e.g. embedded devices).
/// </summary>
public enum EdgeAuthScheme
{
    /// <summary>HMAC-SHA256 signature (default) — third-party apps/devices, secretKey + canonical signing string.</summary>
    Hmac = 0,

    /// <summary>sessionKey — front-end clients (mini-program/H5) after login; short token replacing verbose JWT.</summary>
    Session = 1,

    /// <summary>No authentication — e.g. login endpoints.</summary>
    None = 2,

    /// <summary>MD5 signature — legacy device/client compatibility; same canonical payload as Hmac.</summary>
    Md5 = 3,
}

/// <summary>
/// Edge RPC request envelope — uniform format for all external clients.
///
/// <see cref="Credential"/> is interpreted by <see cref="Scheme"/>:
/// - Hmac:    signature value (Base64)
/// - Session: sessionKey (short login token, format clientId:appId:guid)
/// - Md5:     MD5 hex signature
/// - None:    left empty
/// </summary>
public sealed class EdgeRpcRequest
{
    /// <summary>Registered client system identifier (e.g. "demo-app").</summary>
    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>RPC method (e.g. "demo.ping").</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>Credential scheme — default <see cref="EdgeAuthScheme.Hmac"/>.</summary>
    [JsonPropertyName("scheme")]
    public EdgeAuthScheme Scheme { get; set; } = EdgeAuthScheme.Hmac;

    /// <summary>Credential — signature or sessionKey, per <see cref="Scheme"/>.</summary>
    [JsonPropertyName("credential")]
    public string Credential { get; set; } = string.Empty;

    /// <summary>Unix epoch milliseconds.</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>One-time random string (required in Hmac/Md5 schemes, replay hardening).</summary>
    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    /// <summary>Optional client-generated correlation id — signed, echoed in the response, propagated into server-side tracing.</summary>
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    /// <summary>Method parameters.</summary>
    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }

    /// <summary>Optional contract version (not signed).</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>
/// Edge RPC response envelope.
/// </summary>
public sealed record EdgeRpcResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Fine-grained business sub-code (alipay-style code/subCode layering) — optional.
    /// <see cref="Code"/> is the stable gateway category (params/auth/ratelimit/server semantics);
    /// <see cref="SubCode"/> is the open business vocabulary, conventionally
    /// <c>{domain}.{detail}</c> — e.g. <c>identity.400</c> for an upstream pass-through or
    /// <c>params.redirect_url</c> for a field-level rejection. Omitted when unset.
    /// </summary>
    [JsonPropertyName("subCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubCode { get; init; }

    /// <summary>Supplementary human-readable detail for <see cref="SubCode"/> — optional, omitted when unset.</summary>
    [JsonPropertyName("subMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubMessage { get; init; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    /// <summary>Echo of the request's correlation id, when supplied.</summary>
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    /// <summary>Server-side trace id (W3C) for diagnostics — set by the gateway, not by handlers.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    public static EdgeRpcResponse Success(object? data) => new()
    {
        Ok = true,
        Code = "OK",
        Data = data is null ? null : JsonSerializer.SerializeToElement(data),
    };

    public static EdgeRpcResponse Error(string code, string message) => new()
    {
        Ok = false,
        Code = code,
        Message = message,
    };

    /// <summary>Creates an error carrying a business <see cref="SubCode"/>/<see cref="SubMessage"/> pair.</summary>
    public static EdgeRpcResponse ErrorWithSub(string code, string message, string? subCode = null, string? subMessage = null) => new()
    {
        Ok = false,
        Code = code,
        Message = message,
        SubCode = subCode,
        SubMessage = subMessage,
    };

    /// <summary>Returns a copy carrying the request correlation id and the current trace id.</summary>
    public EdgeRpcResponse WithDiagnostics(EdgeRpcRequest request)
        => this with
        {
            RequestId = request.RequestId,
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString(),
        };
}

/// <summary>
/// Edge JSON options — camelCase, no indentation, string enums. Shared by server and client.
/// </summary>
public static class EdgeJsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
    };
}
