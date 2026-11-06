using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Caching;

namespace PubSoft.Tokens;

public sealed class HttpTokenProvider : ITokenProvider
{
    private const long MaxTokenResponseBytes = 64 * 1024;

    public const string HttpClientName = "PubSoft.Tokens";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICache _cache;
    private readonly IOptionsMonitor<TokenManagerOptions> _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ILogger<HttpTokenProvider>? _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.Ordinal);

    public HttpTokenProvider(
        IHttpClientFactory httpClientFactory,
        ICache cache,
        IOptionsMonitor<TokenManagerOptions> options,
        TimeProvider? timeProvider = null,
        ILogger<HttpTokenProvider>? logger = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<string?> GetTokenAsync(string credentialKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialKey);
        if (!_options.CurrentValue.Credentials.TryGetValue(credentialKey, out var credential))
        {
            _logger?.LogWarning("Token credential is not configured.");
            return null;
        }

        var cacheKey = GetCacheKey(credentialKey);
        var cached = await GetFreshTokenAsync(cacheKey, credential, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
            return cached.Value;

        var refreshLock = _refreshLocks.GetOrAdd(credentialKey, static _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = await GetFreshTokenAsync(cacheKey, credential, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
                return cached.Value;

            var fetched = await FetchTokenAsync(credential, cancellationToken).ConfigureAwait(false);
            if (fetched is null)
                return null;

            var lifetime = fetched.ExpiresIn is { } dynamicExpiry && dynamicExpiry < credential.Expiry
                ? dynamicExpiry
                : credential.Expiry;
            if (lifetime <= TimeSpan.Zero)
                return null;

            var now = _timeProvider.GetUtcNow();
            var refreshWindow = credential.RefreshBefore < TimeSpan.FromTicks(lifetime.Ticks / 2)
                ? credential.RefreshBefore
                : TimeSpan.FromTicks(lifetime.Ticks / 2);
            var entry = new TokenEntry(fetched.Value, now + lifetime, now + lifetime - refreshWindow);
            await _cache.SetAsync(cacheKey, entry, lifetime, cancellationToken).ConfigureAwait(false);
            return entry.Value;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public Task InvalidateAsync(string credentialKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialKey);
        return _cache.RemoveAsync(GetCacheKey(credentialKey), cancellationToken);
    }

    internal static bool IsValidCredential(TokenCredential credential)
    {
        if (credential.Headers is null || string.IsNullOrWhiteSpace(credential.Method)
            || !credential.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && !credential.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(credential.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, System.StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, System.StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrWhiteSpace(credential.TokenPath)
            || credential.TokenPath.Split('.').Any(string.IsNullOrWhiteSpace)
            || credential.Expiry <= TimeSpan.Zero || credential.RefreshBefore < TimeSpan.Zero
            || credential.RefreshBefore >= credential.Expiry || credential.Timeout <= TimeSpan.Zero)
            return false;

        // Credentials are sent to this URL — plaintext http is rejected for
        // non-loopback endpoints unless explicitly opted in per credential.
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, System.StringComparison.Ordinal) && !uri.IsLoopback && !credential.AllowInsecureUrl)
            return false;

        if (credential.ExpiryPath is { } expiryPath
            && (string.IsNullOrWhiteSpace(expiryPath) || expiryPath.Split('.').Any(string.IsNullOrWhiteSpace)))
            return false;

        return credential.Headers.All(pair =>
            !string.IsNullOrWhiteSpace(pair.Key)
            && pair.Value is not null
            && !pair.Key.Contains('\r') && !pair.Key.Contains('\n')
            && !pair.Value.Contains('\r') && !pair.Value.Contains('\n'));
    }

    private async Task<TokenEntry?> GetFreshTokenAsync(
        string cacheKey,
        TokenCredential credential,
        CancellationToken cancellationToken)
    {
        var entry = await _cache.GetAsync<TokenEntry>(cacheKey, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        return entry.RefreshAt > _timeProvider.GetUtcNow() ? entry : null;
    }

    private async Task<FetchedToken?> FetchTokenAsync(TokenCredential credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            credential.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Get : HttpMethod.Post,
            credential.Url);
        if (request.Method == HttpMethod.Post && !string.IsNullOrEmpty(credential.Body))
            request.Content = new StringContent(credential.Body, Encoding.UTF8, "application/json");

        foreach (var (name, value) in credential.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                _logger?.LogWarning("Token request contains an unsupported header.");
                return null;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(credential.Timeout);
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Token endpoint returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            try
            {
                // Bound the endpoint response — token payloads are small JSON; an
                // oversized or chunked body must not exhaust memory mid-parse.
                await response.Content.LoadIntoBufferAsync(MaxTokenResponseBytes, timeout.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                _logger?.LogWarning("Token endpoint response exceeded the size limit.");
                return null;
            }

            await using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: timeout.Token).ConfigureAwait(false);
            if (!TryGetPath(document.RootElement, credential.TokenPath, out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                _logger?.LogWarning("Token response does not contain the configured token field.");
                return null;
            }

            TimeSpan? dynamicExpiry = null;
            if (credential.ExpiryPath is { } expiryPath
                && TryGetPath(document.RootElement, expiryPath, out var expiryElement)
                && expiryElement.TryGetInt64(out var seconds) && seconds > 0)
            {
                try { dynamicExpiry = TimeSpan.FromSeconds(seconds); }
                catch (OverflowException) { dynamicExpiry = credential.Expiry; }
            }

            return new FetchedToken(tokenElement.GetString()!, dynamicExpiry);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Token endpoint request timed out.");
            return null;
        }
        catch (HttpRequestException)
        {
            _logger?.LogWarning("Token endpoint request failed.");
            return null;
        }
        catch (JsonException)
        {
            _logger?.LogWarning("Token endpoint returned invalid JSON.");
            return null;
        }
    }

    private static bool TryGetPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                return false;
        }
        return true;
    }

    private static string GetCacheKey(string credentialKey)
        => $"pubsoft:token:{Convert.ToHexString(Encoding.UTF8.GetBytes(credentialKey))}";

    private sealed record TokenEntry(string Value, DateTimeOffset ExpiresAt, DateTimeOffset RefreshAt);
    private sealed record FetchedToken(string Value, TimeSpan? ExpiresIn);
}