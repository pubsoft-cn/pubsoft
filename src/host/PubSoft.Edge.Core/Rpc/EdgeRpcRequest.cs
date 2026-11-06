using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSoft.Edge;


/// <summary>
/// Edge RPC request envelope -- uniform format for all external clients.
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

    /// <summary>Credential scheme -- default <see cref="EdgeAuthScheme.Hmac"/>.</summary>
    [JsonPropertyName("scheme")]
    public EdgeAuthScheme Scheme { get; set; } = EdgeAuthScheme.Hmac;

    /// <summary>Credential -- signature or sessionKey, per <see cref="Scheme"/>.</summary>
    [JsonPropertyName("credential")]
    public string Credential { get; set; } = string.Empty;

    /// <summary>Unix epoch milliseconds.</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>One-time random string (required in Hmac/Md5 schemes, replay hardening).</summary>
    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    /// <summary>Optional client-generated correlation id -- signed, echoed in the response, propagated into server-side tracing.</summary>
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    /// <summary>Method parameters.</summary>
    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }

    /// <summary>Optional contract version (not signed).</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
