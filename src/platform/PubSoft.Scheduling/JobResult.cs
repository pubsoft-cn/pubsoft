using System.Text.Json;

namespace PubSoft.Scheduling;


[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public sealed class JobResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public int DelayMs { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Signals that a failed result is transient and may be retried when the job
    /// has a <see cref="ScheduledJob.Retry"/> policy. Defaults to false: executors
    /// opt in per failure instead of masking persistent errors behind retries.
    /// </summary>
    public bool Retryable { get; init; }

    public Dictionary<string, object?> Metadata { get; init; } = [];
    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.UtcNow;
}
