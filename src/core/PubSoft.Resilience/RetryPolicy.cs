namespace PubSoft.Resilience;

/// <summary>Retry decision and exponential-backoff policy for HTTP-style failures.</summary>
public sealed class RetryPolicy
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    public bool RetryOnTimeout { get; set; } = true;
    public bool RetryOn5xx { get; set; } = true;
    public bool RetryOnConnectionFailure { get; set; } = true;

    public bool ShouldRetry(Exception? exception, int attempt, int? statusCode)
    {
        if (attempt < 1 || attempt > MaxRetries) return false;

        if (statusCode is >= 500 and < 600) return RetryOn5xx;
        if (statusCode is >= 400 and < 500) return false;
        if (exception is null or OperationCanceledException) return RetryOnTimeout;
        return exception is HttpRequestException && RetryOnConnectionFailure;
    }

    public TimeSpan GetDelay(int attempt)
    {
        var delay = InitialDelay * Math.Pow(2, attempt - 1);
        return TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds, 30));
    }
}
