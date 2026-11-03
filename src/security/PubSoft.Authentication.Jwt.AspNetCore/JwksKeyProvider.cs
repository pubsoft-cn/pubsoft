using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt.AspNetCore;

public sealed class JwksOptions
{
    public const string SectionName = "AuthServer";
    public string Issuer { get; set; } = "";
    public string JwksUri { get; set; } = "";
    public int CacheMinutes { get; set; } = 15;
}

public sealed class JwksKeyProvider
{
    private readonly JwksOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<JwksKeyProvider> _logger;
    private readonly HttpClient _httpClient;
    private static readonly ConcurrentDictionary<string, SecurityKey> _keyCache = new();

    public JwksKeyProvider(IOptions<JwksOptions> options, IMemoryCache cache,
        ILogger<JwksKeyProvider> logger, HttpClient httpClient)
    {
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<SecurityKey?> ResolveSigningKeyAsync(string keyId, CancellationToken ct = default)
    {
        if (_keyCache.TryGetValue(keyId, out var cached))
            return cached;
        var keys = await GetJwksAsync(ct).ConfigureAwait(false);
        if (keys is null) return null;
        foreach (var key in keys)
            if (key.KeyId == keyId)
            {
                _keyCache[keyId] = key;
                return key;
            }
        return null;
    }

    private async Task<SecurityKey[]?> GetJwksAsync(CancellationToken ct)
    {
        const string cacheKey = "jwks_keys";
        if (_cache.TryGetValue(cacheKey, out SecurityKey[]? cached) && cached is not null)
            return cached;
        try
        {
            var set = await _httpClient.GetFromJsonAsync<JsonWebKeySet>(NormalizeUri(_options.JwksUri), ct).ConfigureAwait(false);
            var keys = set?.GetSigningKeys()?.ToArray() ?? Array.Empty<SecurityKey>();
            if (keys.Length > 0)
                _cache.Set(cacheKey, keys, TimeSpan.FromMinutes(_options.CacheMinutes));
            return keys;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWKS fetch failed from {Uri}", _options.JwksUri);
            return null;
        }
    }

    private static string NormalizeUri(string uri)
        => uri.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? uri : $"{uri.TrimEnd('/')}/.well-known/jwks.json";
}
