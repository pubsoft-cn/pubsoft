using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>
/// <see cref="IClientCredentialVerifier"/> forwarding the client id + secret pair to a centralized
/// credential registry over HTTP. Wire contract: <c>POST { "clientId", "clientSecret" }</c>;
/// a 2xx body of <c>{ "success": true, "clientId", "tenantId"?, "claims"? }</c> verifies the pair.
/// Any non-2xx response, malformed payload, timeout, or transport error fails closed.
/// </summary>
/// <remarks>
/// The secret still crosses the wire to the registry -- prefer signature/assertion modes where
/// supported; this verifier exists to converge services onto a single credential source of truth.
/// </remarks>
public sealed class RemoteClientCredentialVerifier : IClientCredentialVerifier
{
    private readonly HttpClient _httpClient;
    private readonly RemoteClientCredentialOptions _options;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<RemoteClientCredentialVerifier>? _logger;

    /// <summary>Creates the verifier.</summary>
    public RemoteClientCredentialVerifier(
        HttpClient httpClient,
        RemoteClientCredentialOptions options,
        IMemoryCache? cache = null,
        ILogger<RemoteClientCredentialVerifier>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ClientCredentialVerificationResult> VerifyAsync(
        string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        var cacheKey = _cache is not null && _options.CacheMinutes > 0
            ? $"pubsoft:rcc:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId + "\0" + clientSecret)))}"
            : null;
        if (cacheKey is not null && _cache!.TryGetValue(cacheKey, out ClientCredentialVerificationResult? cached) && cached is not null)
            return cached;

        var result = await VerifyRemoteAsync(clientId, clientSecret, cancellationToken).ConfigureAwait(false);
        if (cacheKey is not null)
            _cache!.Set(cacheKey, result, TimeSpan.FromMinutes(_options.CacheMinutes));
        return result;
    }

    private async Task<ClientCredentialVerificationResult> VerifyRemoteAsync(
        string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
            {
                Content = JsonContent.Create(new VerifyRequest(clientId, clientSecret)),
            };
            _options.ConfigureRequest?.Invoke(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return ClientCredentialVerificationResult.Fail();

            var body = await response.Content.ReadFromJsonAsync<VerifyResponse>(cancellationToken).ConfigureAwait(false);
            if (body is not { Success: true } || string.IsNullOrWhiteSpace(body.ClientId))
                return ClientCredentialVerificationResult.Fail();

            return ClientCredentialVerificationResult.Ok(body.ClientId, body.TenantId, body.Claims);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Remote client credential verification failed against {Endpoint}", _options.Endpoint);
            return ClientCredentialVerificationResult.Fail();
        }
    }

    private sealed record VerifyRequest(string ClientId, string ClientSecret);

    private sealed class VerifyResponse
    {
        public bool Success { get; init; }
        public string? ClientId { get; init; }
        public long? TenantId { get; init; }
        public IReadOnlyDictionary<string, string>? Claims { get; init; }
    }
}
