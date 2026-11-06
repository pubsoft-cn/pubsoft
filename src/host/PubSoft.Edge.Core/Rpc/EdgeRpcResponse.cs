using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSoft.Edge;


/// <summary>
/// Edge RPC response envelope.
/// </summary>
public sealed record EdgeRpcResponse
{
    /// <summary>Whether the call succeeded.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>Machine-readable result code.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>Human-readable detail.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Fine-grained business sub-code (alipay-style code/subCode layering) -- optional.
    /// <see cref="Code"/> is the stable gateway category (params/auth/ratelimit/server semantics);
    /// <see cref="SubCode"/> is the open business vocabulary, conventionally
    /// <c>{domain}.{detail}</c> -- e.g. <c>identity.400</c> for an upstream pass-through or
    /// <c>params.redirect_url</c> for a field-level rejection. Omitted when unset.
    /// </summary>
    [JsonPropertyName("subCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubCode { get; init; }

    /// <summary>Supplementary human-readable detail for <see cref="SubCode"/> -- optional, omitted when unset.</summary>
    [JsonPropertyName("subMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubMessage { get; init; }

    /// <summary>Response payload on success.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    /// <summary>Echo of the request's correlation id, when supplied.</summary>
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    /// <summary>Server-side trace id (W3C) for diagnostics -- set by the gateway, not by handlers.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    /// <summary>Creates a success response.</summary>
    public static EdgeRpcResponse Success(object? data) => new()
    {
        Ok = true,
        Code = "OK",
        Data = data is null ? null : JsonSerializer.SerializeToElement(data),
    };

    /// <summary>Creates an error response.</summary>
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
