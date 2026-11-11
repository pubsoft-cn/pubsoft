using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;

/// <summary>
/// Configuration-backed <see cref="IJwtIssuerKeyRing"/> for hosts that source PEM material from
/// settings/secret stores rather than a dedicated KMS. Validation is fail-closed at construction:
/// an empty ring, a missing active key id, malformed PEM, or keys below 2048 bits all throw.
/// </summary>
public sealed class ConfigJwtIssuerKeyRing : IJwtIssuerKeyRing
{
    private readonly RsaSigningKeyMaterial[] _keys;
    private bool _disposed;

    /// <summary>Creates the ring; imports and validates every configured PEM.</summary>
    public ConfigJwtIssuerKeyRing(JwtIssuerKeyRingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ActiveKeyId);
        ArgumentNullException.ThrowIfNull(options.Keys);
        if (options.Keys.Count == 0)
            throw new ArgumentException("At least one issuer signing key is required.", nameof(options));
        if (!options.Keys.ContainsKey(options.ActiveKeyId))
            throw new ArgumentException($"ActiveKeyId '{options.ActiveKeyId}' has no entry in Keys.", nameof(options));

        var keys = new List<RsaSigningKeyMaterial>(options.Keys.Count);
        try
        {
            RsaSigningKeyMaterial? active = null;
            foreach (var (keyId, pem) in options.Keys)
            {
                RsaSigningKeyMaterial material;
                try
                {
                    material = JwtSigning.ImportFromPem(keyId, pem);
                }
                catch (Exception ex) when (ex is ArgumentException or CryptographicException)
                {
                    throw new ArgumentException($"Issuer key '{keyId}' is not a usable RSA private key: {ex.Message}", nameof(options), ex);
                }
                keys.Add(material);
                if (string.Equals(keyId, options.ActiveKeyId, StringComparison.Ordinal))
                    active = material;
            }
            ActiveKey = active!;
        }
        catch
        {
            foreach (var material in keys)
                material.Dispose();
            throw;
        }
        _keys = [.. keys];
    }

    /// <inheritdoc />
    public RsaSigningKeyMaterial ActiveKey { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<RsaSigningKeyMaterial> PublishedKeys => _keys;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var material in _keys)
            material.Dispose();
    }
}
