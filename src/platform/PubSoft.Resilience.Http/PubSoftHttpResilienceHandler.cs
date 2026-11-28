using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Resilience.Http;

/// <summary>DelegatingHandler applying per-attempt timeout, retry, and circuit breaking per host.</summary>
public sealed class PubSoftHttpResilienceHandler : DelegatingHandler
{
    private readonly HttpResilienceOptions _options;
    private readonly CircuitBreaker? _circuitBreaker;
    private readonly ILogger<PubSoftHttpResilienceHandler>? _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a handler with its resilience policy and clock used for Retry-After date evaluation.</summary>
    public PubSoftHttpResilienceHandler(
        HttpResilienceOptions options,
        CircuitBreaker? circuitBreaker = null,
        ILogger<PubSoftHttpResilienceHandler>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!_options.IsValid())
            throw new ArgumentException("HTTP resilience options are invalid.", nameof(options));

        _circuitBreaker = circuitBreaker;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal static TimeSpan? GetRetryAfterDelay(HttpResponseMessage response, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta > TimeSpan.Zero ? delta : null;
        if (retryAfter?.Date is { } date)
        {
            var delay = date - now;
            return delay > TimeSpan.Zero ? delay : null;
        }
        return null;
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
        var startedAt = _timeProvider.GetUtcNow();
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
                    var status = (int)response.StatusCode;
                    if (status >= 500 || status == 408 || status == 429)
                        RecordFailure(circuitKey);
                    else
                        _circuitBreaker?.RecordSuccess(circuitKey);
                    completed = true;
                    return response;
                }

                var now = _timeProvider.GetUtcNow();
                var retryAfter = GetRetryAfterDelay(response, now);
                var remainingTotalTimeout = _options.Timeout - (now - startedAt);
                if (retryAfter >= remainingTotalTimeout)
                {
                    RecordFailure(circuitKey);
                    completed = true;
                    return response;
                }

                response.Dispose();
                _logger?.LogDebug("HTTP resilience retry {Attempt} for {Host} after status {StatusCode}.",
                    attempt, circuitKey, (int)response.StatusCode);
                await DelayBeforeRetryAsync(circuitKey, attempt, totalTimeout.Token, cancellationToken, retryAfter).ConfigureAwait(false);
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
        CancellationToken callerToken,
        TimeSpan? serverMinimumDelay = null)
    {
        var retry = _options.Retry!;
        var delay = retry.GetDelayWithJitter(attempt);
        if (serverMinimumDelay is { } minimum && minimum > delay)
            delay = minimum;
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

/// <summary>HttpClient resilience registration.</summary>
public static class HttpResilienceServiceCollectionExtensions
{
    /// <summary>Adds the resilience handler with default options.</summary>
    public static IHttpClientBuilder AddPubSoftHttpResilience(this IHttpClientBuilder builder)
        => builder.AddPubSoftHttpResilience(configure: null);

    /// <summary>Adds the resilience handler with configured options.</summary>
    public static IHttpClientBuilder AddPubSoftHttpResilience(
        this IHttpClientBuilder builder,
        Action<HttpResilienceOptions>? configure)
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
                JitterRatio = options.Retry.JitterRatio,
                RetryOnTimeout = options.Retry.RetryOnTimeout,
                RetryOn5xx = options.Retry.RetryOn5xx,
                RetryOnRequestTimeout = options.Retry.RetryOnRequestTimeout,
                RetryOnTooManyRequests = options.Retry.RetryOnTooManyRequests,
                RetryOnConnectionFailure = options.Retry.RetryOnConnectionFailure
            };
        options.CircuitBreaker = options.CircuitBreaker is null
            ? null
            : new CircuitBreakerOptions
            {
                Mode = options.CircuitBreaker.Mode,
                FailureThreshold = options.CircuitBreaker.FailureThreshold,
                FailureRatio = options.CircuitBreaker.FailureRatio,
                MinimumThroughput = options.CircuitBreaker.MinimumThroughput,
                SamplingDuration = options.CircuitBreaker.SamplingDuration,
                OpenTimeout = options.CircuitBreaker.OpenTimeout
            };

        var circuitBreaker = options.CircuitBreaker is null
            ? null
            : new CircuitBreaker { Options = options.CircuitBreaker };

        builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
        builder.AddHttpMessageHandler(provider => new PubSoftHttpResilienceHandler(
            options,
            circuitBreaker,
            provider.GetService<ILogger<PubSoftHttpResilienceHandler>>(),
            provider.GetService<TimeProvider>()));
        return builder;
    }

    /// <summary>
    /// Binds <see cref="HttpResilienceOptions"/> from <see cref="IConfigurationSection"/> and registers the resilience handler.
    /// Kept for compatibility with the older <c>AddWireResilience(section)</c> usage.
    /// </summary>
    public static IHttpClientBuilder AddPubSoftHttpResilience(
        this IHttpClientBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);
        return builder.AddPubSoftHttpResilience(o => BindHttpResilienceOptions(section, o));
    }

    /// <summary>
    /// Explicit per-key binding (the reflection-based configuration binder is not AOT-safe). Timeouts use
    /// flattened millisecond keys (<c>TimeoutMs</c>, <c>Retry:InitialDelayMs</c>, ...); unknown keys are ignored.
    /// </summary>
    internal static void BindHttpResilienceOptions(IConfiguration section, HttpResilienceOptions options)
    {
        if (int.TryParse(section["TimeoutMs"], out var t)) options.Timeout = TimeSpan.FromMilliseconds(t);
        if (int.TryParse(section["PerAttemptTimeoutMs"], out var pat)) options.PerAttemptTimeout = TimeSpan.FromMilliseconds(pat);
        if (int.TryParse(section["Retry:MaxRetries"], out var mr)) { options.Retry ??= new(); options.Retry.MaxRetries = mr; }
        if (int.TryParse(section["Retry:InitialDelayMs"], out var idl)) { options.Retry ??= new(); options.Retry.InitialDelay = TimeSpan.FromMilliseconds(idl); }
        if (double.TryParse(section["Retry:JitterRatio"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var jr)) { options.Retry ??= new(); options.Retry.JitterRatio = jr; }
        if (bool.TryParse(section["Retry:RetryOnTimeout"], out var rt)) { options.Retry ??= new(); options.Retry.RetryOnTimeout = rt; }
        if (bool.TryParse(section["Retry:RetryOn5xx"], out var r5)) { options.Retry ??= new(); options.Retry.RetryOn5xx = r5; }
        if (bool.TryParse(section["Retry:RetryOnRequestTimeout"], out var rrt)) { options.Retry ??= new(); options.Retry.RetryOnRequestTimeout = rrt; }
        if (bool.TryParse(section["Retry:RetryOnTooManyRequests"], out var rtmr)) { options.Retry ??= new(); options.Retry.RetryOnTooManyRequests = rtmr; }
        if (bool.TryParse(section["Retry:RetryOnConnectionFailure"], out var rcf)) { options.Retry ??= new(); options.Retry.RetryOnConnectionFailure = rcf; }
        if (Enum.TryParse<CircuitBreakerMode>(section["CircuitBreaker:Mode"], ignoreCase: true, out var mode)) { options.CircuitBreaker ??= new(); options.CircuitBreaker.Mode = mode; }
        if (int.TryParse(section["CircuitBreaker:FailureThreshold"], out var ft)) { options.CircuitBreaker ??= new(); options.CircuitBreaker.FailureThreshold = ft; }
        if (double.TryParse(section["CircuitBreaker:FailureRatio"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fr)) { options.CircuitBreaker ??= new(); options.CircuitBreaker.FailureRatio = fr; }
        if (int.TryParse(section["CircuitBreaker:MinimumThroughput"], out var mt)) { options.CircuitBreaker ??= new(); options.CircuitBreaker.MinimumThroughput = mt; }
        if (int.TryParse(section["CircuitBreaker:SamplingDurationMs"], out var sd)) { options.CircuitBreaker ??= new(); options.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(sd); }
        if (int.TryParse(section["CircuitBreaker:OpenTimeoutMs"], out var ot)) { options.CircuitBreaker ??= new(); options.CircuitBreaker.OpenTimeout = TimeSpan.FromMilliseconds(ot); }
    }
}
