using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt.AspNetCore;


/// <summary>Resolves signing keys from a cached JWKS document.</summary>
public sealed class JwksKeyProvider
{
    private readonly JwksOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<JwksKeyProvider> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>Creates the provider.</summary>
    public JwksKeyProvider(IOptions<JwksOptions> options, IMemoryCache cache,
        ILogger<JwksKeyProvider> logger, HttpClient httpClient)
    {
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>Finds a key by id inside the cached JWKS; null when absent.</summary>
    public async Task<SecurityKey?> ResolveSigningKeyAsync(string keyId, CancellationToken ct = default)
    {
        // Resolved keys share the JWKS set's CacheMinutes lifetime - a per-key
        // cache that outlived the set would keep accepting revoked key IDs.
        var keys = await GetJwksAsync(ct).ConfigureAwait(false);
        if (keys is null) return null;
        foreach (var key in keys)
            if (string.Equals(key.KeyId, keyId, System.StringComparison.Ordinal))
                return key;
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
            // Empty sets are cached too - a key-removal response must hold for the
            // TTL, and caching avoids refetching once per token while JWKS is empty.
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
