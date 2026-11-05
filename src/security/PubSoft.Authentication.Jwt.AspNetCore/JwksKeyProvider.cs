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
    /// <summary>
    /// Expected token audience for the AuthServer scheme. When set, audience is
    /// validated; when empty it is not — configure it whenever the issuer mints
    /// tokens for more than one API.
    /// </summary>
    public string Audience { get; set; } = "";
    public int CacheMinutes { get; set; } = 15;
    /// <summary>
    /// Permits a non-https <see cref="JwksUri"/> outside loopback. Default false —
    /// a plaintext JWKS fetch lets an on-path attacker substitute signing keys.
    /// </summary>
    public bool AllowInsecureJwksUri { get; set; }
}

public sealed class JwksKeyProvider
{
    private readonly JwksOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<JwksKeyProvider> _logger;
    private readonly HttpClient _httpClient;

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
        // Resolved keys share the JWKS set's CacheMinutes lifetime — a per-key
        // cache that outlived the set would keep accepting revoked key IDs.
        var keys = await GetJwksAsync(ct).ConfigureAwait(false);
        if (keys is null) return null;
        foreach (var key in keys)
            if (key.KeyId == keyId)
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
            // Empty sets are cached too — a key-removal response must hold for the
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
