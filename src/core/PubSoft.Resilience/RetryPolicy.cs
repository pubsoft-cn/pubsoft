namespace PubSoft.Resilience;

/// <summary>Retry decision and exponential-backoff policy for HTTP-style failures.</summary>
public sealed class RetryPolicy
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
    private double _jitterRatio = 0.2;

    /// <summary>Maximum retry attempts after the first call.</summary>
    public int MaxRetries { get; set; } = 3;
    /// <summary>Base delay; later attempts back off exponentially.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>
    /// Bounded symmetric jitter applied by <see cref="GetDelayWithJitter"/> as a ratio of the exponential delay.
    /// 0 disables jitter; 0.2 means a delay uniformly sampled in the 80%-120% band. Values above 1 are rejected.
    /// </summary>
    public double JitterRatio
    {
        get => _jitterRatio;
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Jitter ratio must be a finite value between 0 and 1.");
            _jitterRatio = value;
        }
    }
    /// <summary>Whether timeouts are retried.</summary>
    public bool RetryOnTimeout { get; set; } = true;
    /// <summary>Whether 5xx responses are retried.</summary>
    public bool RetryOn5xx { get; set; } = true;
    /// <summary>Whether HTTP 408 Request Timeout responses are retried. Explicit opt-in because some servers use 408 for non-transient request shaping.</summary>
    public bool RetryOnRequestTimeout { get; set; }
    /// <summary>Whether HTTP 429 Too Many Requests responses are retried. Explicit opt-in; Retry-After is honored as a minimum delay when present.</summary>
    public bool RetryOnTooManyRequests { get; set; }
    /// <summary>Whether connection failures are retried.</summary>
    public bool RetryOnConnectionFailure { get; set; } = true;

    /// <summary>Whether the given failure on attempt <paramref name="attempt"/> is retryable.</summary>
    public bool ShouldRetry(Exception? exception, int attempt, int? statusCode)
    {
        if (attempt < 1 || attempt > MaxRetries) return false;

        if (statusCode is >= 500 and < 600) return RetryOn5xx;
        if (statusCode == 408) return RetryOnRequestTimeout;
        if (statusCode == 429) return RetryOnTooManyRequests;
        if (statusCode is >= 400 and < 500) return false;
        if (exception is null or OperationCanceledException) return RetryOnTimeout;
        return exception is HttpRequestException && RetryOnConnectionFailure;
    }

    /// <summary>Deterministic base delay before the next attempt (exponential backoff, capped at 30 seconds).</summary>
    public TimeSpan GetDelay(int attempt)
    {
        var delay = InitialDelay * Math.Pow(2, attempt - 1);
        return TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds, MaxDelay.TotalSeconds));
    }

    /// <summary>
    /// Delay before the next attempt with bounded symmetric jitter applied around <see cref="GetDelay"/>.
    /// The final delay remains capped at 30 seconds; use <see cref="GetDelay"/> when a deterministic base value is needed.
    /// </summary>
    public TimeSpan GetDelayWithJitter(int attempt)
    {
        var baseDelay = GetDelay(attempt);
        if (baseDelay == TimeSpan.Zero || JitterRatio == 0)
            return baseDelay;

        var factor = 1 - JitterRatio + (Random.Shared.NextDouble() * 2 * JitterRatio);
        var jitteredTicks = (long)Math.Round(baseDelay.Ticks * factor, MidpointRounding.AwayFromZero);
        return TimeSpan.FromTicks(Math.Clamp(jitteredTicks, 0, MaxDelay.Ticks));
    }
}
