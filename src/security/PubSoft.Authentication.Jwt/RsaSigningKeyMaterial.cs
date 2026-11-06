using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;

public sealed class RsaSigningKeyMaterial : IDisposable
{
    private bool _disposed;

    internal RsaSigningKeyMaterial(string keyId, RSA rsa)
    {
        KeyId = keyId;
        Rsa = rsa;
        SecurityKey = new RsaSecurityKey(rsa) { KeyId = keyId };
    }

    public string KeyId { get; }
    public RSA Rsa { get; }
    public RsaSecurityKey SecurityKey { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        Rsa.Dispose();
        _disposed = true;
    }
}
