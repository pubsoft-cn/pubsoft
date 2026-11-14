using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;

/// <summary>RSA signing key material held for token issuance; dispose to release the key.</summary>
public sealed class RsaSigningKeyMaterial : IDisposable
{
    private bool _disposed;

    internal RsaSigningKeyMaterial(string keyId, RSA rsa)
    {
        KeyId = keyId;
        Rsa = rsa;
        SecurityKey = new RsaSecurityKey(rsa) { KeyId = keyId };
    }

    /// <summary>Key id.</summary>
    public string KeyId { get; }
    /// <summary>The RSA key.</summary>
    public RSA Rsa { get; }
    /// <summary>SecurityKey wrapper.</summary>
    public RsaSecurityKey SecurityKey { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        Rsa.Dispose();
        _disposed = true;
    }
}
