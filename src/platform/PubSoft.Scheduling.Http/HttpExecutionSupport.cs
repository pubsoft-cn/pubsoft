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
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
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

    public async Task<HttpAttemptResult> SendAsync(
        HttpExecutorConfig config,
        Uri uri,
        HttpMethod method,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (method != HttpMethod.Get && !string.IsNullOrEmpty(config.Body))
                request.Content = new StringContent(config.Body, Encoding.UTF8, "application/json");

            foreach (var (name, value) in config.Headers)
            {
                if (!request.Headers.TryAddWithoutValidation(name, value)
                    && (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(name, value)))
                    return HttpAttemptResult.Failure(0, stopwatch.Elapsed, "invalid request header");
            }

            if (config.Token is { } tokenRef)
            {
                if (_tokenProvider is null)
                    return HttpAttemptResult.Failure(0, stopwatch.Elapsed, "token provider is not registered");

                var token = await _tokenProvider.GetTokenAsync(tokenRef.CredentialKey, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                    return HttpAttemptResult.Failure(0, stopwatch.Elapsed, "token unavailable");

                request.Headers.Remove(tokenRef.HeaderName);
                if (!request.Headers.TryAddWithoutValidation(tokenRef.HeaderName, tokenRef.HeaderPrefix + token))
                    return HttpAttemptResult.Failure(0, stopwatch.Elapsed, "invalid token header");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(config.Timeout ?? _options.DefaultTimeout);
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);

            var statusCode = (int)response.StatusCode;
            var succeeded = config.ExpectStatusCodes.Contains(statusCode);
            if (!succeeded)
                _logger?.LogDebug("HTTP scheduler request returned status {StatusCode}.", statusCode);
            return HttpAttemptResult.Response(succeeded, statusCode, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("HTTP scheduler request timed out.");
            return HttpAttemptResult.Failure(0, stopwatch.Elapsed, "timeout", new OperationCanceledException());
        }
        catch (HttpRequestException exception)
        {
            _logger?.LogDebug(exception, "HTTP scheduler request failed.");
            return HttpAttemptResult.Failure(0, stopwatch.Elapsed, "request failed", exception);
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