using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;

/// <summary>
/// <see cref="IClientAssertionKeyProvider"/> resolving client public keys from a per-client JWKS
/// document (RFC 7517) pulled from the credential registry through a host-injected fetch delegate.
/// </summary>
/// <remarks>
/// Cache semantics differ from escrowed symmetric-key lanes on purpose: an expired set is never
/// served stale - a rotated-out key must stop verifying, not linger for availability. Fetches are
/// single-flight per client id; failed or empty results are negatively cached for
/// <see cref="JwksClientAssertionKeyOptions.MissCacheDuration"/> to bound probing cost. JWKs
/// without a <c>kid</c> are skipped (key ids are required for rotation); a kid-less assertion
/// resolves only when the set holds exactly one key.
/// </remarks>
public sealed class JwksClientAssertionKeyProvider : IClientAssertionKeyProvider, IDisposable
{
    private readonly JwksClientAssertionKeyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>Creates the provider.</summary>
    public JwksClientAssertionKeyProvider(JwksClientAssertionKeyOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<RsaSigningKeyMaterial?> GetPublicKeyAsync(
        string clientId, string? keyId, CancellationToken cancellationToken = default)
    {
        var entry = await GetEntryAsync(clientId, cancellationToken).ConfigureAwait(false);
        var keys = entry?.Keys;
        if (keys is null || keys.Count == 0)
            return null;
        if (keyId is not null)
            return keys.FirstOrDefault(k => string.Equals(k.KeyId, keyId, StringComparison.Ordinal));
        return keys.Count == 1 ? keys[0] : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var entry in _entries.Values)
            if (entry.Keys is not null)
                foreach (var material in entry.Keys)
                    material.Dispose();
        foreach (var gate in _locks.Values)
            gate.Dispose();
        _entries.Clear();
        _locks.Clear();
    }

    private async Task<CacheEntry?> GetEntryAsync(string clientId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(clientId, out var entry) && entry.ExpiresAt > now)
            return entry;

        var gate = _locks.GetOrAdd(clientId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_entries.TryGetValue(clientId, out entry) && entry.ExpiresAt > _timeProvider.GetUtcNow())
                return entry;

            IReadOnlyList<RsaSigningKeyMaterial>? keys = null;
            try
            {
                var jwks = await _options.FetchJwks!(clientId, cancellationToken).ConfigureAwait(false);
                keys = Parse(jwks);
            }
            catch (Exception) // pubsoft:exempt PUBSOFT041 "fetch/parse failure becomes a negative-cache miss; fail closed by design"
            {
            }

            var next = new CacheEntry(
                _timeProvider.GetUtcNow() + (keys is { Count: > 0 } ? _options.CacheDuration : _options.MissCacheDuration),
                keys);
            if (_entries.TryRemove(clientId, out var old) && old.Keys is not null)
                foreach (var material in old.Keys)
                    material.Dispose();
            _entries[clientId] = next;
            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    private static IReadOnlyList<RsaSigningKeyMaterial>? Parse(string? jwksJson)
    {
        if (string.IsNullOrWhiteSpace(jwksJson))
            return null;

        JsonWebKeySet set;
        try
        {
            set = new JsonWebKeySet(jwksJson);
        }
        catch (Exception)
        {
            return null;
        }

        var keys = new List<RsaSigningKeyMaterial>();
        foreach (var jwk in set.Keys)
        {
            // GetSigningKeys() converts to RsaSecurityKey and drops the JWK view - filter the
            // raw set: RSA signing keys only (use must be "sig" or absent per RFC 7517).
            if (jwk.Use is not null && !string.Equals(jwk.Use, "sig", StringComparison.Ordinal))
                continue;
            if (!string.Equals(jwk.Kty, "RSA", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(jwk.Kid) || jwk.Kid.Length > 128 || jwk.Kid.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(jwk.N) || string.IsNullOrWhiteSpace(jwk.E))
                continue;
            try
            {
                var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = Base64UrlEncoder.DecodeBytes(jwk.N),
                    Exponent = Base64UrlEncoder.DecodeBytes(jwk.E),
                });
                keys.Add(JwtSigning.ImportRsaPublicKey(jwk.Kid, rsa));
                rsa.Dispose(); // ImportRsaPublicKey re-imports into its own instance.
            }
            catch (Exception) // pubsoft:exempt PUBSOFT041 "malformed JWK is skipped; an empty yield becomes a miss entry"
            {
            }
        }
        return keys.Count > 0 ? keys : null;
    }

    private sealed record CacheEntry(DateTimeOffset ExpiresAt, IReadOnlyList<RsaSigningKeyMaterial>? Keys);
}
