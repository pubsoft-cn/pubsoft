using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Cryptography;

/// <summary>
/// Client-side request signer for the X-Client-* protocol: HMAC-SHA256 over
/// <c>{method}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}</c>, base64 signature,
/// unix-millisecond timestamp. Server-side counterpart is
/// <c>PubSoft.Security.HmacSignatureValidator</c> -- wire profiles differ
/// (hex/seconds there), so configure both ends deliberately.
/// </summary>
public static class ClientSigner
{
    /// <summary>Allowed timestamp skew between client and server clocks.</summary>
    public static readonly TimeSpan TimestampTolerance = TimeSpan.FromMinutes(5);
    /// <summary>Replay window a nonce is expected to be retained for.</summary>
    public static readonly TimeSpan NonceTtl = TimeSpan.FromMinutes(10);

    /// <summary>Header carrying the client identifier.</summary>
    public const string HeaderClientId = "X-Client-Id";
    /// <summary>Header carrying the unix-millisecond timestamp.</summary>
    public const string HeaderTimestamp = "X-Timestamp";
    /// <summary>Header carrying the per-request nonce.</summary>
    public const string HeaderNonce = "X-Nonce";
    /// <summary>Header carrying the base64 HMAC-SHA256 signature.</summary>
    public const string HeaderSignature = "X-Signature";

    /// <summary>Builds the canonical string and returns the base64 signature.</summary>
    public static string Sign(string clientSecret, string method, string path, string timestamp, string nonce, string bodyHash)
    {
        var stringToSign = $"{method}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clientSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Verifies a client-signed request against the shared secret using the signer's own wire profile.</summary>
    public static bool Verify(string clientSecret, string method, string path, string timestamp, string nonce, string bodyHash, string signature)
    {
        var expected = Sign(clientSecret, method, path, timestamp, nonce, bodyHash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    /// <summary>Computes the canonical body hash used by the signing profile (null/empty bodies map to the empty-body hash).</summary>
    public static string ComputeBodyHash(byte[]? body)
    {
        if (body == null || body.Length == 0) return "";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(body);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Whether the timestamp parses and falls inside the accepted window.</summary>
    public static bool IsTimestampValid(string timestamp, TimeProvider? timeProvider = null)
    {
        if (!long.TryParse(timestamp, out var ts)) return false;
        var dto = DateTimeOffset.FromUnixTimeMilliseconds(ts);
        var diff = (timeProvider ?? TimeProvider.System).GetUtcNow() - dto;
        return Math.Abs(diff.TotalSeconds) <= TimestampTolerance.TotalSeconds;
    }
}
