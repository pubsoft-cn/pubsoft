namespace PubSoft.Resilience.Http;

/// <summary>Per-request resilience policy for the HTTP resilience handler.</summary>
public sealed class HttpResilienceOptions
{
    /// <summary>Overall request deadline including retries.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(8);
    /// <summary>Deadline for a single attempt.</summary>
    public TimeSpan PerAttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Retry policy; null disables retries.</summary>
    public RetryPolicy? Retry { get; set; }
    /// <summary>Circuit-breaker policy; null disables breaking.</summary>
    public CircuitBreakerOptions? CircuitBreaker { get; set; }

    internal bool IsValid()
        => Timeout > TimeSpan.Zero
            && PerAttemptTimeout > TimeSpan.Zero
            && PerAttemptTimeout <= Timeout
            && (Retry is null || (Retry.MaxRetries >= 0 && Retry.InitialDelay >= TimeSpan.Zero))
            && (CircuitBreaker is null
                || (Enum.IsDefined(CircuitBreaker.Mode)
                    && CircuitBreaker.FailureThreshold > 0
                    && CircuitBreaker.OpenTimeout > TimeSpan.Zero
                    && (CircuitBreaker.Mode != CircuitBreakerMode.RollingFailureRatio
                        || (CircuitBreaker.FailureRatio > 0
                            && CircuitBreaker.FailureRatio <= 1
                            && CircuitBreaker.MinimumThroughput > 0
                            && CircuitBreaker.SamplingDuration > TimeSpan.Zero))));
}