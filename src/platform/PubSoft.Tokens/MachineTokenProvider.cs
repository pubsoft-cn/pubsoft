using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PubSoft.Authentication.Jwt;

namespace PubSoft.Tokens;

public sealed class MachineTokenCredential
{
    public Dictionary<string, string> Claims { get; set; } = new(StringComparer.Ordinal);
    public string? SigningKey { get; set; }
    public string? KeyId { get; set; }
}

public sealed class MachineTokenOptions
{
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public required string SigningKey { get; set; }
    public string KeyId { get; set; } = "machine";
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromMinutes(5);
    public Dictionary<string, MachineTokenCredential> Credentials { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MachineTokenProvider : ITokenProvider, IDisposable
{
    private readonly MachineTokenOptions _options;
    private readonly SymmetricSigningKeyMaterial _keyMaterial;
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _cache = new(StringComparer.Ordinal);

    public MachineTokenProvider(IOptions<MachineTokenOptions> options)
    {
        _options = options.Value;
        _keyMaterial = JwtSigning.ImportSymmetricKey(_options.KeyId, _options.SigningKey);
    }

    public Task<string?> GetTokenAsync(string credentialKey, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
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

    public Task InvalidateAsync(string credentialKey, CancellationToken cancellationToken = default)
    {
        _cache.TryRemove(credentialKey, out _);
        return Task.CompletedTask;
    }

    public void Dispose() => _keyMaterial.Dispose();
}
