using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;


/// <summary>
/// Explicitly selected symmetric (HMAC) JWT signing key. This exists for hosts that
/// deliberately run symmetric service-to-service tokens — the algorithm is always an
/// explicit choice and is never a fallback from an asymmetric path.
/// </summary>
public sealed class SymmetricSigningKeyMaterial : IDisposable
{
    private readonly byte[] _keyBytes;
    private bool _disposed;

    internal SymmetricSigningKeyMaterial(string keyId, byte[] keyBytes)
    {
        KeyId = keyId;
        _keyBytes = keyBytes;
        SecurityKey = new SymmetricSecurityKey(keyBytes) { KeyId = keyId };
    }

    public string KeyId { get; }
    public SymmetricSecurityKey SecurityKey { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        CryptographicOperations.ZeroMemory(_keyBytes);
        _disposed = true;
    }
}
