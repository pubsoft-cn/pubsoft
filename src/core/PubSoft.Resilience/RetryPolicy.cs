namespace PubSoft.Resilience;

/// <summary>Retry decision and exponential-backoff policy for HTTP-style failures.</summary>
public sealed class RetryPolicy
{
    /// <summary>Maximum retry attempts after the first call.</summary>
    public int MaxRetries { get; set; } = 3;
    /// <summary>Base delay; later attempts back off exponentially.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Whether timeouts are retried.</summary>
    public bool RetryOnTimeout { get; set; } = true;
    /// <summary>Whether 5xx responses are retried.</summary>
    public bool RetryOn5xx { get; set; } = true;
    /// <summary>Whether connection failures are retried.</summary>
    public bool RetryOnConnectionFailure { get; set; } = true;

    /// <summary>Whether the given failure on attempt <paramref name="attempt"/> is retryable.</summary>
    public bool ShouldRetry(Exception? exception, int attempt, int? statusCode)
    {
        if (attempt < 1 || attempt > MaxRetries) return false;

        if (statusCode is >= 500 and < 600) return RetryOn5xx;
        if (statusCode is >= 400 and < 500) return false;
        if (exception is null or OperationCanceledException) return RetryOnTimeout;
        return exception is HttpRequestException && RetryOnConnectionFailure;
    }

    /// <summary>Delay before the next attempt (exponential backoff).</summary>
    public TimeSpan GetDelay(int attempt)
    {
        var delay = InitialDelay * Math.Pow(2, attempt - 1);
        return TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds, 30));
    }
}
