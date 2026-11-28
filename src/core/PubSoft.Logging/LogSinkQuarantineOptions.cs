namespace PubSoft.Logging;

/// <summary>
/// Failure-quarantine tuning for log sinks. A sink that keeps throwing is paused for a bounded,
/// doubling backoff instead of being retried on every batch; after the pause a single probe batch
/// decides whether it recovers or stays quarantined.
/// </summary>
public sealed class LogSinkQuarantineOptions
{
    /// <summary>Consecutive failed batches that engage the quarantine. Default 3.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>First quarantine pause; doubles on each failed probe up to <see cref="MaxBackoff"/>. Default 1 second.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound of the quarantine pause. Default 30 seconds.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate(string paramName)
    {
        if (FailureThreshold <= 0)
            throw new ArgumentException("Failure threshold must be positive.", paramName);
        if (InitialBackoff <= TimeSpan.Zero)
            throw new ArgumentException("Initial backoff must be positive.", paramName);
        if (MaxBackoff < InitialBackoff)
            throw new ArgumentException("Maximum backoff must not be smaller than the initial backoff.", paramName);
    }
}
