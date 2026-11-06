using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PubSoft.Edge;

/// <summary>
/// Edge RPC signing — canonical signing string over the envelope fields.
///
/// Canonical payload (newline-separated):
///   method
///   timestamp
///   nonce
///   requestId          (empty line when absent)
///   canonical_json(params)
///
/// Hmac:     Base64(HMAC-SHA256(payload, secretKey))
/// Md5:      HexLower(MD5(payload + secretKey))        — legacy scheme for clients that cannot use HMAC.
///
/// The canonicalizer addresses the failure modes of ad-hoc sorted-parameter signing:
/// recursive ordinal key sorting, UTF-8, compact JSON, null/undefined excluded
/// (0/false/"" still participate), library-independent output.
/// </summary>
public static class EdgeSigner
{
    #region HMAC

    /// <summary>Computes the HMAC-SHA256 signature (Base64) for the given request fields.</summary>
    public static string SignHmac(string method, long timestamp, string nonce, string? requestId, object? parameters, string secretKey)
    {
        var payload = BuildSignPayload(method, timestamp, nonce, requestId, parameters);
        return ComputeHmacSha256(payload, secretKey);
    }

    /// <summary>Verifies an HMAC-SHA256 signature in constant time.</summary>
    public static bool VerifyHmac(string method, long timestamp, string nonce, string? requestId, object? parameters, string secretKey, string signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        var payload = BuildSignPayload(method, timestamp, nonce, requestId, parameters);
        var expected = ComputeHmacSha256(payload, secretKey);
        return FixedTimeEquals(signature, expected);
    }

    /// <summary>Computes the MD5 hex signature (legacy scheme) for the given request fields.</summary>
    public static string SignMd5(string method, long timestamp, string nonce, string? requestId, object? parameters, string secretKey)
    {
        var payload = BuildSignPayload(method, timestamp, nonce, requestId, parameters) + secretKey;
        return ComputeMd5Hex(payload);
    }

    /// <summary>Verifies an MD5 hex signature in constant time.</summary>
    public static bool VerifyMd5(string method, long timestamp, string nonce, string? requestId, object? parameters, string secretKey, string signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        var expected = SignMd5(method, timestamp, nonce, requestId, parameters, secretKey);
        return FixedTimeEquals(signature, expected);
    }

    /// <summary>
    /// Builds the canonical signing payload (for debugging/logging).
    /// Never includes the secret key.
    /// </summary>
    public static string BuildSignPayload(string method, long timestamp, string nonce, string? requestId, object? parameters)
    {
        var canonicalJson = CanonicalizeJson(parameters);
        return $"{method}\n{timestamp}\n{nonce}\n{requestId ?? string.Empty}\n{canonicalJson}";
    }

    #endregion

    #region Unified verification

    /// <summary>
    /// Verifies <see cref="EdgeRpcRequest.Credential"/> per <see cref="EdgeRpcRequest.Scheme"/>.
    /// Session scheme is validated by the session manager, not here.
    /// </summary>
    public static EdgeSignResult Verify(EdgeRpcRequest request, string secretKey)
    {
        return request.Scheme switch
        {
            EdgeAuthScheme.Hmac => VerifySignature(request, secretKey, isMd5: false),
            EdgeAuthScheme.Md5 => VerifySignature(request, secretKey, isMd5: true),
            EdgeAuthScheme.Session => new EdgeSignResult(false, "Session scheme is validated by EdgeSessionManager", null),
            EdgeAuthScheme.None => new EdgeSignResult(true, null, null),
            _ => new EdgeSignResult(false, "Unknown scheme", null),
        };
    }

    private static EdgeSignResult VerifySignature(EdgeRpcRequest request, string secretKey, bool isMd5)
    {
        if (string.IsNullOrEmpty(request.Nonce))
            return new EdgeSignResult(false, "nonce is required for signature schemes", null);

        var ok = isMd5
            ? VerifyMd5(request.Method, request.Timestamp, request.Nonce, request.RequestId, request.Params, secretKey, request.Credential)
            : VerifyHmac(request.Method, request.Timestamp, request.Nonce, request.RequestId, request.Params, secretKey, request.Credential);
        if (ok)
            return new EdgeSignResult(true, null, null);

        var payload = BuildSignPayload(request.Method, request.Timestamp, request.Nonce, request.RequestId, request.Params);
        return new EdgeSignResult(false, isMd5 ? "MD5 signature verification failed" : "HMAC signature verification failed", payload);
    }

    #endregion

    #region Canonical JSON

    /// <summary>
    /// Canonical JSON — recursive ordinal key sort, compact, null/undefined excluded.
    /// 0/false/"" participate; only null/undefined are dropped.
    /// </summary>
    public static string CanonicalizeJson(object? value) // pubsoft:exempt PUBSOFT032 "#region grouping is the ordering mechanism here"
    {
        if (value is null)
            return "{}";

        var element = value is JsonElement je
            ? je
            : JsonSerializer.SerializeToElement(value, CanonicalJsonOptions);

        return CanonicalizeElement(element);
    }

    private static string CanonicalizeElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => CanonicalizeObject(element),
            JsonValueKind.Array => CanonicalizeArray(element),
            JsonValueKind.Null => "null",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String => CanonicalizeString(element),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.Undefined => "null",
            _ => element.GetRawText(),
        };
    }

    private static string CanonicalizeObject(JsonElement element)
    {
        var props = new List<(string Key, string CanonicalValue)>();
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Null || prop.Value.ValueKind == JsonValueKind.Undefined)
                continue;

            props.Add((prop.Name, CanonicalizeElement(prop.Value)));
        }

        props.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        var sb = new StringBuilder();
        sb.Append('{');
        for (var i = 0; i < props.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(EscapeString(props[i].Key));
            sb.Append(':');
            sb.Append(props[i].CanonicalValue);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string CanonicalizeArray(JsonElement element)
    {
        var items = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null || item.ValueKind == JsonValueKind.Undefined)
                continue;

            items.Add(CanonicalizeElement(item));
        }

        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(string.Join(",", items));
        sb.Append(']');
        return sb.ToString();
    }

    private static string CanonicalizeString(JsonElement element)
    {
        return JsonSerializer.Serialize(element.GetString());
    }

    private static string EscapeString(string s) => JsonSerializer.Serialize(s);

    #endregion

    #region Hashing

    private static string ComputeHmacSha256(string payload, string secretKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToBase64String(hash);
    }

    private static string ComputeMd5Hex(string payload)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++)
            result |= a[i] ^ b[i];

        return result == 0;
    }

    #endregion

    #region JSON options

    internal static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web) // pubsoft:exempt PUBSOFT031 "#region grouping is the ordering mechanism here"
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    #endregion
}

/// <summary>
/// Signature verification result — carries the canonical payload for debugging.
/// </summary>
public sealed record EdgeSignResult(bool Success, string? ErrorMessage, string? SignPayload);
