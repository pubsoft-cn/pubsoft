using PubSoft.Resilience;

namespace PubSoft.Resilience.Http;

public sealed class HttpResilienceOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(8);
    public TimeSpan PerAttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public RetryPolicy? Retry { get; set; }
    public CircuitBreakerOptions? CircuitBreaker { get; set; }

    internal bool IsValid()
        => Timeout > TimeSpan.Zero
            && PerAttemptTimeout > TimeSpan.Zero
            && PerAttemptTimeout <= Timeout
            && (Retry is null || (Retry.MaxRetries >= 0 && Retry.InitialDelay >= TimeSpan.Zero))
            && (CircuitBreaker is null
                || (CircuitBreaker.FailureThreshold > 0 && CircuitBreaker.OpenTimeout > TimeSpan.Zero));
}