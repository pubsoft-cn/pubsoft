using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using PubSoft.Resilience;

namespace PubSoft.Resilience.Http;

public sealed class PubSoftHttpResilienceHandler : DelegatingHandler
{
    private readonly HttpResilienceOptions _options;
    private readonly CircuitBreaker? _circuitBreaker;
    private readonly ILogger<PubSoftHttpResilienceHandler>? _logger;

    public PubSoftHttpResilienceHandler(
        HttpResilienceOptions options,
        CircuitBreaker? circuitBreaker = null,
        ILogger<PubSoftHttpResilienceHandler>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!_options.IsValid())
            throw new ArgumentException("HTTP resilience options are invalid.", nameof(options));

        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var circuitKey = request.RequestUri?.IdnHost ?? "unknown";
        if (_circuitBreaker is not null && !_circuitBreaker.TryAcquire(circuitKey))
            throw new HttpRequestException("The HTTP circuit is open.");

        var completed = false;
        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalTimeout.CancelAfter(_options.Timeout);

        try
        {
            var attempt = 0;
            while (true)
            {
                attempt++;
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token);
                attemptTimeout.CancelAfter(_options.PerAttemptTimeout);

                HttpResponseMessage response;
                try
                {
                    var currentRequest = attempt == 1 ? request : CloneWithoutContent(request);
                    response = await base.SendAsync(currentRequest, attemptTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException exception) when (totalTimeout.IsCancellationRequested)
                {
                    RecordFailure(circuitKey);
                    completed = true;
                    throw new TaskCanceledException("The HTTP request exceeded its total timeout.", exception);
                }
                catch (OperationCanceledException exception)
                {
                    if (CanRetry(request) && _options.Retry?.ShouldRetry(exception, attempt, null) == true)
                    {
                        await DelayBeforeRetryAsync(circuitKey, attempt, totalTimeout.Token, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    RecordFailure(circuitKey);
                    completed = true;
                    throw new TaskCanceledException("The HTTP request exceeded its per-attempt timeout.", exception);
                }
                catch (HttpRequestException exception)
                {
                    if (CanRetry(request) && _options.Retry?.ShouldRetry(exception, attempt, null) == true)
                    {
                        _logger?.LogDebug("HTTP resilience retry {Attempt} for {Host} after a connection failure.", attempt, circuitKey);
                        await DelayBeforeRetryAsync(circuitKey, attempt, totalTimeout.Token, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    RecordFailure(circuitKey);
                    completed = true;
                    throw;
                }
                catch
                {
                    RecordFailure(circuitKey);
                    completed = true;
                    throw;
                }

                if (response.IsSuccessStatusCode)
                {
                    _circuitBreaker?.RecordSuccess(circuitKey);
                    completed = true;
                    return response;
                }

                if (!CanRetry(request)
                    || _options.Retry?.ShouldRetry(null, attempt, (int)response.StatusCode) != true)
                {
                    RecordFailure(circuitKey);
                    completed = true;
                    return response;
                }

                response.Dispose();
                _logger?.LogDebug("HTTP resilience retry {Attempt} for {Host} after status {StatusCode}.",
                    attempt, circuitKey, (int)response.StatusCode);
                await DelayBeforeRetryAsync(circuitKey, attempt, totalTimeout.Token, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!completed)
                _circuitBreaker?.Abandon(circuitKey);
        }
    }

    private async Task DelayBeforeRetryAsync(
        string circuitKey,
        int attempt,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        var retry = _options.Retry!;
        var delay = retry.GetDelay(attempt);
        try
        {
            await Task.Delay(delay, timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            RecordFailure(circuitKey);
            throw new TaskCanceledException("The HTTP request exceeded its total timeout.", exception);
        }
    }

    private void RecordFailure(string circuitKey) => _circuitBreaker?.RecordFailure(circuitKey);

    private static bool CanRetry(HttpRequestMessage request)
        => request.Content is null
            && (request.Method == HttpMethod.Get
                || request.Method == HttpMethod.Head
                || request.Method == HttpMethod.Options
                || request.Method == HttpMethod.Trace);

    private static HttpRequestMessage CloneWithoutContent(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in original.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        return clone;
    }
}

public static class HttpResilienceServiceCollectionExtensions
{
    public static IHttpClientBuilder AddPubSoftHttpResilience(
        this IHttpClientBuilder builder,
        Action<HttpResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new HttpResilienceOptions();
        configure?.Invoke(options);
        if (!options.IsValid())
            throw new ArgumentException("HTTP resilience options are invalid.", nameof(configure));

        options.Retry = options.Retry is null
            ? null
            : new RetryPolicy
            {
                MaxRetries = options.Retry.MaxRetries,
                InitialDelay = options.Retry.InitialDelay,
                RetryOnTimeout = options.Retry.RetryOnTimeout,
                RetryOn5xx = options.Retry.RetryOn5xx,
                RetryOnConnectionFailure = options.Retry.RetryOnConnectionFailure
            };
        options.CircuitBreaker = options.CircuitBreaker is null
            ? null
            : new CircuitBreakerOptions
            {
                FailureThreshold = options.CircuitBreaker.FailureThreshold,
                OpenTimeout = options.CircuitBreaker.OpenTimeout
            };

        var circuitBreaker = options.CircuitBreaker is null
            ? null
            : new CircuitBreaker { Options = options.CircuitBreaker };

        builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
        builder.AddHttpMessageHandler(provider => new PubSoftHttpResilienceHandler(
            options,
            circuitBreaker,
            provider.GetService<ILogger<PubSoftHttpResilienceHandler>>()));
        return builder;
    }
}