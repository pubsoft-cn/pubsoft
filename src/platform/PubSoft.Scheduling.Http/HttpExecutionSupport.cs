using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Scheduling;
using PubSoft.Tokens;

namespace PubSoft.Scheduling.Http;

internal sealed class HttpExecutionSupport
{
    public const string HttpClientName = "PubSoft.Scheduling.Http";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenProvider? _tokenProvider;
    private readonly HttpExecutorOptions _options;
    private readonly ILogger<HttpExecutionSupport>? _logger;

    public HttpExecutionSupport(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpExecutorOptions> options,
        ITokenProvider? tokenProvider = null,
        ILogger<HttpExecutionSupport>? logger = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public bool TryReadConfig(
        ScheduledJob job,
        out HttpExecutorConfig config,
        out Uri uri,
        out HttpMethod method)
    {
        config = new HttpExecutorConfig();
        uri = null!;
        method = HttpMethod.Get;

        try
        {
            config = job.ExecutorConfig.Deserialize(SchedulingHttpJsonContext.Default.HttpExecutorConfig)!;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (config is null || !Uri.TryCreate(config.Url, UriKind.Absolute, out uri!)
            || !IsValidTarget(uri)
            || !TryCreateMethod(config.Method, out method)
            || (config.Timeout ?? _options.DefaultTimeout) <= TimeSpan.Zero
            || config.Headers is null
            || config.Headers.Any(header => string.IsNullOrWhiteSpace(header.Key)
                || header.Key.Contains('\r') || header.Key.Contains('\n')
                || header.Value is null || header.Value.Contains('\r') || header.Value.Contains('\n'))
            || config.ExpectStatusCodes is null || config.ExpectStatusCodes.Length == 0
            || config.ExpectStatusCodes.Any(status => status is < 100 or > 599)
            || config.Retry is { } retry && (retry.MaxRetries < 0 || retry.InitialDelay < TimeSpan.Zero)
            || config.Token is { } token && (string.IsNullOrWhiteSpace(token.CredentialKey)
                || string.IsNullOrWhiteSpace(token.HeaderName)
                || token.HeaderName.Contains('\r') || token.HeaderName.Contains('\n')
                || token.HeaderPrefix is null || token.HeaderPrefix.Contains('\r') || token.HeaderPrefix.Contains('\n')))
            return false;

        return true;
    }

    /// <summary>Target rules shared by the initial URL and every redirect hop.</summary>
    private bool IsValidTarget(Uri uri)
        => (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo)
            && HttpEgressGuard.IsHostAllowed(uri.IdnHost, _options.AllowedHosts)
            && (!_options.BlockPrivateNetworks || !HttpEgressGuard.IsBlockedHostLiteral(uri));

    private const int MaxRedirects = 8;

    public async Task<HttpAttemptResult> SendAsync(
        HttpExecutorConfig config,
        Uri uri,
        HttpMethod method,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var target = uri;
        var sendMethod = method;
        var sendBody = method != HttpMethod.Get;
        var dropTokenHeader = false;
        var redirectsLeft = _options.FollowRedirects ? MaxRedirects : 0;

        // Redirects are followed manually so every hop re-runs target validation
        // (allowlist + literal private-IP) and the credential header is stripped
        // when the redirect crosses to a different host.
        while (true)
        {
            var (result, location) = await SendOnceAsync(
                config, target, sendMethod, sendBody, dropTokenHeader, stopwatch, cancellationToken)
                .ConfigureAwait(false);
            if (location is null || !_options.FollowRedirects)
                return result;

            if (redirectsLeft <= 0)
                return HttpAttemptResult.Failure(0, stopwatch.Elapsed, "redirect limit exceeded");
            redirectsLeft--;

            var next = location.IsAbsoluteUri ? location : new Uri(target, location);
            if (!IsValidTarget(next))
                return HttpAttemptResult.Failure(0, stopwatch.Elapsed, "redirect target blocked");

            if (!string.Equals(target.IdnHost, next.IdnHost, StringComparison.OrdinalIgnoreCase))
                dropTokenHeader = true;

            // RFC 7231: 303 downgrades to GET; 301/302 do the same for POST (HttpClient parity).
            if ((int)result.StatusCode == 303 && sendMethod != HttpMethod.Head
                || (int)result.StatusCode is 301 or 302 && sendMethod == HttpMethod.Post)
            {
                sendMethod = HttpMethod.Get;
                sendBody = false;
            }
            target = next;
        }
    }

    /// <summary>Single attempt; returns the result plus the redirect Location (when followable).</summary>
    private async Task<(HttpAttemptResult Result, Uri? Location)> SendOnceAsync(
        HttpExecutorConfig config,
        Uri uri,
        HttpMethod method,
        bool sendBody,
        bool dropTokenHeader,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (sendBody && !string.IsNullOrEmpty(config.Body))
                request.Content = new StringContent(config.Body, Encoding.UTF8, "application/json");

            foreach (var (name, value) in config.Headers)
            {
                if (!request.Headers.TryAddWithoutValidation(name, value)
                    && (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(name, value)))
                    return (HttpAttemptResult.Failure(0, stopwatch.Elapsed, "invalid request header"), null);
            }

            if (config.Token is { } tokenRef && !dropTokenHeader)
            {
                if (_tokenProvider is null)
                    return (HttpAttemptResult.Failure(0, stopwatch.Elapsed, "token provider is not registered"), null);

                var token = await _tokenProvider.GetTokenAsync(tokenRef.CredentialKey, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                    return (HttpAttemptResult.Failure(0, stopwatch.Elapsed, "token unavailable"), null);

                request.Headers.Remove(tokenRef.HeaderName);
                if (!request.Headers.TryAddWithoutValidation(tokenRef.HeaderName, tokenRef.HeaderPrefix + token))
                    return (HttpAttemptResult.Failure(0, stopwatch.Elapsed, "invalid token header"), null);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(config.Timeout ?? _options.DefaultTimeout);
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);

            var statusCode = (int)response.StatusCode;
            if (_options.FollowRedirects
                && statusCode is 301 or 302 or 303 or 307 or 308
                && response.Headers.Location is { } location)
                return (HttpAttemptResult.Response(false, statusCode, stopwatch.Elapsed), location);

            var succeeded = config.ExpectStatusCodes.Contains(statusCode);
            if (!succeeded)
                _logger?.LogDebug("HTTP scheduler request returned status {StatusCode}.", statusCode);
            return (HttpAttemptResult.Response(succeeded, statusCode, stopwatch.Elapsed), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("HTTP scheduler request timed out.");
            return (HttpAttemptResult.Failure(0, stopwatch.Elapsed, "timeout", new OperationCanceledException()), null);
        }
        catch (HttpRequestException exception)
        {
            _logger?.LogDebug(exception, "HTTP scheduler request failed.");
            return (HttpAttemptResult.Failure(0, stopwatch.Elapsed, "request failed", exception), null);
        }
    }

    public static string GetCircuitKey(Uri uri)
    {
        var hostHash = SHA256.HashData(Encoding.UTF8.GetBytes(uri.IdnHost.ToLowerInvariant()));
        return Convert.ToHexString(hostHash);
    }

    private static bool TryCreateMethod(string? methodName, out HttpMethod method)
    {
        method = HttpMethod.Get;
        if (string.IsNullOrWhiteSpace(methodName)
            || methodName.Any(character => !IsHttpTokenCharacter(character)))
            return false;

        method = new HttpMethod(methodName);
        return true;
    }

    private static bool IsHttpTokenCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character);
}

internal sealed record HttpAttemptResult(
    bool Success,
    int StatusCode,
    int DelayMs,
    string? ErrorMessage,
    Exception? Exception)
{
    public static HttpAttemptResult Response(bool success, int statusCode, TimeSpan elapsed)
        => new(success, statusCode, (int)elapsed.TotalMilliseconds, null, null);

    public static HttpAttemptResult Failure(int statusCode, TimeSpan elapsed, string error, Exception? exception = null)
        => new(false, statusCode, (int)elapsed.TotalMilliseconds, error, exception);

    public JobResult ToJobResult()
        => new()
        {
            Success = Success,
            StatusCode = StatusCode,
            DelayMs = DelayMs,
            ErrorMessage = ErrorMessage
        };
}