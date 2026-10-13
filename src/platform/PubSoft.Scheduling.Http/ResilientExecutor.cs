using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Resilience;
using PubSoft.Scheduling;
using PubSoft.Tokens;

namespace PubSoft.Scheduling.Http;

public sealed class ResilientExecutor : IJobExecutor
{
    private readonly HttpExecutionSupport _http;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly ILogger<ResilientExecutor>? _logger;

    public ResilientExecutor(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpExecutorOptions> options,
        CircuitBreaker circuitBreaker,
        ITokenProvider? tokenProvider = null,
        ILogger<ResilientExecutor>? logger = null)
    {
        _http = new HttpExecutionSupport(httpClientFactory, options, tokenProvider);
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
        _logger = logger;
    }

    public string Type => "Http.Resilient";

    public async Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        if (!_http.TryReadConfig(job, out var config, out var uri, out var method))
            return new JobResult { Success = false, ErrorMessage = "Invalid HttpExecutorConfig" };

        cancellationToken.ThrowIfCancellationRequested();
        var circuitKey = HttpExecutionSupport.GetCircuitKey(uri);
        if (!_circuitBreaker.TryAcquire(circuitKey))
            return new JobResult { Success = false, ErrorMessage = "circuit open" };

        var stopwatch = Stopwatch.StartNew();
        var retries = config.Retry;
        var attemptNumber = 0;
        var finalized = false;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attemptNumber++;
                var attempt = await _http.SendAsync(config, uri, method, cancellationToken).ConfigureAwait(false);
                if (attempt.Success)
                {
                    _circuitBreaker.RecordSuccess(circuitKey);
                    finalized = true;
                    return ToJobResult(attempt, stopwatch.Elapsed);
                }

                if (retries is null
                    || !config.AllowUnsafeRetries && !IsSafeToRetry(method, config.Body)
                    || !retries.ShouldRetry(attempt.Exception, attemptNumber, attempt.StatusCode == 0 ? null : attempt.StatusCode))
                {
                    _circuitBreaker.RecordFailure(circuitKey);
                    finalized = true;
                    return ToJobResult(attempt, stopwatch.Elapsed);
                }

                var delay = retries.GetDelay(attemptNumber);
                _logger?.LogDebug("HTTP scheduler retry {Attempt} after {DelayMs} ms.", attemptNumber, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!finalized)
                _circuitBreaker.Abandon(circuitKey);
        }
    }

    private static JobResult ToJobResult(HttpAttemptResult attempt, TimeSpan elapsed)
        => new()
        {
            Success = attempt.Success,
            StatusCode = attempt.StatusCode,
            DelayMs = (int)elapsed.TotalMilliseconds,
            ErrorMessage = attempt.ErrorMessage
        };

    private static bool IsSafeToRetry(HttpMethod method, string? body)
        => string.IsNullOrEmpty(body)
            && (method == HttpMethod.Get
                || method == HttpMethod.Head
                || method == HttpMethod.Options
                || method == HttpMethod.Trace);
}