using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Cryptography;

public static class ClientSigner
{
    public static readonly TimeSpan TimestampTolerance = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NonceTtl = TimeSpan.FromMinutes(10);

    public const string HeaderClientId = "X-Client-Id";
    public const string HeaderTimestamp = "X-Timestamp";
    public const string HeaderNonce = "X-Nonce";
    public const string HeaderSignature = "X-Signature";

    public static string Sign(string clientSecret, string method, string path, string timestamp, string nonce, string bodyHash)
    {
        var stringToSign = $"{method}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clientSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(bytes);
    }

    public static bool Verify(string clientSecret, string method, string path, string timestamp, string nonce, string bodyHash, string signature)
    {
        var expected = Sign(clientSecret, method, path, timestamp, nonce, bodyHash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    public static string ComputeBodyHash(byte[]? body)
    {
        if (body == null || body.Length == 0) return "";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(body);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsTimestampValid(string timestamp)
    {
        if (!long.TryParse(timestamp, out var ts)) return false;
        var dto = DateTimeOffset.FromUnixTimeMilliseconds(ts);
        var diff = TimeProvider.System.GetUtcNow() - dto;
        return Math.Abs(diff.TotalSeconds) <= TimestampTolerance.TotalSeconds;
    }
}
