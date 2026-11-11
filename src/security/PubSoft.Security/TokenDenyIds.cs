using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Security;

/// <summary>Stable token-id helpers shared by issuers and validators.</summary>
public static class TokenDenyIds
{
    /// <summary>SHA-256 hex of a raw token string - denylist id for compact JWTs and opaque tokens.</summary>
    /// <param name="token">Raw token string.</param>
    public static string ForToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
