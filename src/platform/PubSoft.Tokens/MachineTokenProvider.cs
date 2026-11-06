using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PubSoft.Authentication.Jwt;

namespace PubSoft.Tokens;


/// <summary>Issues machine JWTs locally with per-credential-key claims and caching.</summary>
public sealed class MachineTokenProvider : ITokenProvider, IDisposable
{
    /// <summary>Keyed-DI service key for resolving this provider when multiple <see cref="ITokenProvider"/> implementations coexist.</summary>
    public const string ServiceKey = "machine";
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private readonly MachineTokenOptions _options;
    private readonly SymmetricSigningKeyMaterial _keyMaterial;
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _cache = new(StringComparer.Ordinal);

    /// <summary>Creates the provider.</summary>
    public MachineTokenProvider(IOptions<MachineTokenOptions> options)
    {
        _options = options.Value;
        _keyMaterial = JwtSigning.ImportSymmetricKey(_options.KeyId, _options.SigningKey);
    }

    /// <inheritdoc />
    public Task<string?> GetTokenAsync(string credentialKey, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        if (_cache.TryGetValue(credentialKey, out var cached) && cached.ExpiresAt - _options.RefreshBefore > now)
            return Task.FromResult<string?>(cached.Token);

        var claims = _options.Credentials.TryGetValue(credentialKey, out var credential)
            ? credential.Claims
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var keyMaterial = credential?.SigningKey is { } key
            ? JwtSigning.ImportSymmetricKey(credential.KeyId ?? _options.KeyId, key)
            : _keyMaterial;
        var token = JwtSigning.CreateAccessToken(keyMaterial, _options.Issuer, _options.Audience, claims, _options.Lifetime);
        _cache[credentialKey] = (token, now.Add(_options.Lifetime));
        return Task.FromResult<string?>(token);
    }

    /// <inheritdoc />
    public Task InvalidateAsync(string credentialKey, CancellationToken cancellationToken = default)
    {
        _cache.TryRemove(credentialKey, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _keyMaterial.Dispose();
}
