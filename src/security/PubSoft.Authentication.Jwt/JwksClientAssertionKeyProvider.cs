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
/// served stale - a rotated-out key must stop verifying, not linger for availability. Cached sets
/// are immutable public-parameter snapshots materialized into key material per lookup, so a
/// refresh can never dispose a key a verifier is using. Fetches are single-flight over a fixed
/// stripe of gates; failed or empty results are negatively cached for
/// <see cref="JwksClientAssertionKeyOptions.MissCacheDuration"/> to bound probing cost. The cache
/// is bounded by <see cref="JwksClientAssertionKeyOptions.MaxCacheEntries"/> - probing many unique
/// client ids cannot grow memory without limit. JWKs without a <c>kid</c> are skipped (key ids are
/// required for rotation); a kid-less assertion resolves only when the set holds exactly one key.
/// </remarks>
public sealed class JwksClientAssertionKeyProvider : IClientAssertionKeyProvider, IDisposable
{
    private const int LockStripes = 64;
    private const int MaxClientIdLength = 256;

    private readonly JwksClientAssertionKeyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim[] _locks =
        Enumerable.Range(0, LockStripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

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
        if (string.IsNullOrWhiteSpace(clientId)
            || clientId.Length > MaxClientIdLength
            || clientId.Any(char.IsControl))
            return null;

        var entry = await GetEntryAsync(clientId, cancellationToken).ConfigureAwait(false);
        var keys = entry?.Keys;
        if (keys is null || keys.Count == 0)
            return null;
        var snapshot = keyId is not null
            ? keys.FirstOrDefault(k => string.Equals(k.KeyId, keyId, StringComparison.Ordinal))
            : keys.Count == 1 ? keys[0] : null;
        if (snapshot is null)
            return null;
        try
        {
            return Materialize(snapshot);
        }
        catch (Exception) // pubsoft:exempt PUBSOFT041 "corrupt cached parameters resolve like a miss; fail closed"
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var gate in _locks)
            gate.Dispose();
        _entries.Clear();
    }

    private async Task<CacheEntry?> GetEntryAsync(string clientId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(clientId, out var entry) && entry.ExpiresAt > now)
            return entry;

        var gate = _locks[(int)((uint)StringComparer.Ordinal.GetHashCode(clientId) % LockStripes)];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_entries.TryGetValue(clientId, out entry) && entry.ExpiresAt > _timeProvider.GetUtcNow())
                return entry;

            IReadOnlyList<KeySnapshot>? keys = null;
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
            _entries[clientId] = next;
            EvictOverflow(clientId);
            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    // Concurrency can overshoot the cap by at most the number of in-flight refreshes -
    // bounded by the lock stripes, which is the point of the bound.
    private void EvictOverflow(string keepClientId)
    {
        if (_entries.Count <= _options.MaxCacheEntries)
            return;
        string? oldest = null;
        var oldestExpiry = DateTimeOffset.MaxValue;
        foreach (var pair in _entries)
        {
            if (string.Equals(pair.Key, keepClientId, StringComparison.Ordinal)
                || pair.Value.ExpiresAt >= oldestExpiry)
                continue;
            oldestExpiry = pair.Value.ExpiresAt;
            oldest = pair.Key;
        }
        if (oldest is not null)
            _entries.TryRemove(oldest, out _);
    }

    private static RsaSigningKeyMaterial Materialize(KeySnapshot snapshot)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(snapshot.Parameters);
        return JwtSigning.ImportRsaPublicKey(snapshot.KeyId, rsa);
    }

    private static IReadOnlyList<KeySnapshot>? Parse(string? jwksJson)
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

        var keys = new List<KeySnapshot>();
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
                var parameters = new RSAParameters
                {
                    Modulus = Base64UrlEncoder.DecodeBytes(jwk.N),
                    Exponent = Base64UrlEncoder.DecodeBytes(jwk.E),
                };
                using var rsa = RSA.Create();
                rsa.ImportParameters(parameters); // validate now so bad keys never reach the cache
                if (rsa.KeySize < 2048)
                    continue; // same floor as ImportRsaPublicKey - weak keys are not cached
                keys.Add(new KeySnapshot(jwk.Kid, parameters));
            }
            catch (Exception) // pubsoft:exempt PUBSOFT041 "malformed JWK is skipped; an empty yield becomes a miss entry"
            {
            }
        }
        return keys.Count > 0 ? keys : null;
    }

    private sealed record KeySnapshot(string KeyId, RSAParameters Parameters);

    private sealed record CacheEntry(DateTimeOffset ExpiresAt, IReadOnlyList<KeySnapshot>? Keys);
}
