using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace PubSoft.Tokens;

public sealed class TokenDelegatingHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;
    private readonly TokenHandlerOptions _options;
    private readonly string? _clientCredentialKey;
    private readonly string? _clientHeaderName;
    private readonly string? _clientHeaderPrefix;

    public TokenDelegatingHandler(ITokenProvider tokenProvider, IOptions<TokenHandlerOptions> options)
        : this(tokenProvider, options, null, null, null)
    {
    }

    public TokenDelegatingHandler(
        ITokenProvider tokenProvider,
        IOptions<TokenHandlerOptions> options,
        string? credentialKey,
        string? headerName,
        string? headerPrefix)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clientCredentialKey = credentialKey;
        _clientHeaderName = headerName;
        _clientHeaderPrefix = headerPrefix;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var credentialKey = request.Options.TryGetValue(TokenRequestOptions.CredentialKey, out var requestKey)
            ? requestKey
            : _clientCredentialKey ?? _options.DefaultCredentialKey;
        if (string.IsNullOrWhiteSpace(credentialKey))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var headerName = request.Options.TryGetValue(TokenRequestOptions.HeaderName, out var requestHeader)
            ? requestHeader
            : _clientHeaderName ?? _options.DefaultHeaderName ?? "Authorization";
        var headerPrefix = request.Options.TryGetValue(TokenRequestOptions.HeaderPrefix, out var requestPrefix)
            ? requestPrefix
            : _clientHeaderPrefix ?? _options.DefaultHeaderPrefix ?? "Bearer ";
        if (string.IsNullOrWhiteSpace(headerName) || headerName.Contains('\r') || headerName.Contains('\n')
            || headerPrefix is null
            || headerPrefix.Contains('\r') || headerPrefix.Contains('\n'))
            throw new InvalidOperationException("Token header configuration is invalid.");

        var token = await _tokenProvider.GetTokenAsync(credentialKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
            throw new HttpRequestException("An access token could not be obtained for the configured credential.");

        SetTokenHeader(request, headerName, headerPrefix, token);
        var retryOnUnauthorized = request.Options.TryGetValue(TokenRequestOptions.RetryOnUnauthorized, out var retryEnabled)
            && retryEnabled;
        using var retryRequest = retryOnUnauthorized
            ? await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false)
            : null;
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized || retryRequest is null)
            return response;

        await _tokenProvider.InvalidateAsync(credentialKey, cancellationToken).ConfigureAwait(false);
        var refreshedToken = await _tokenProvider.GetTokenAsync(credentialKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(refreshedToken))
            return response;

        response.Dispose();
        SetTokenHeader(retryRequest, headerName, headerPrefix, refreshedToken);
        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private static void SetTokenHeader(HttpRequestMessage request, string headerName, string prefix, string token)
    {
        request.Headers.Remove(headerName);
        if (!request.Headers.TryAddWithoutValidation(headerName, prefix + token))
            throw new InvalidOperationException("Token header could not be added to the request.");
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        try
        {
            if (request.Content is not null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                clone.Content = new ByteArrayContent(bytes);
                foreach (var header in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var option in request.Options)
                clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            return clone;
        }
        catch
        {
            clone.Dispose();
            throw;
        }
    }
}

public static class TokenRequestOptions
{
    internal static readonly HttpRequestOptionsKey<string?> CredentialKey = new("PubSoft.Tokens.CredentialKey");
    internal static readonly HttpRequestOptionsKey<string?> HeaderName = new("PubSoft.Tokens.HeaderName");
    internal static readonly HttpRequestOptionsKey<string?> HeaderPrefix = new("PubSoft.Tokens.HeaderPrefix");
    internal static readonly HttpRequestOptionsKey<bool> RetryOnUnauthorized = new("PubSoft.Tokens.RetryOnUnauthorized");

    public static HttpRequestMessage EnableTokenUnauthorizedRetry(this HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(RetryOnUnauthorized, true);
        return request;
    }

    public static HttpRequestMessage SetTokenCredentialKey(this HttpRequestMessage request, string credentialKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialKey);
        request.Options.Set(CredentialKey, credentialKey);
        return request;
    }

    public static HttpRequestMessage SetTokenHeaderName(this HttpRequestMessage request, string headerName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        request.Options.Set(HeaderName, headerName);
        return request;
    }

    public static HttpRequestMessage SetTokenHeaderPrefix(this HttpRequestMessage request, string prefix)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(prefix);
        request.Options.Set(HeaderPrefix, prefix);
        return request;
    }
}