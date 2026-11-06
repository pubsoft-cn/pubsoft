using System.Text.Json;

namespace PubSoft.Scheduling;


/// <summary>Outcome of one job run.</summary>
[PubSoft.Core.BoundedCost("data object - timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public sealed class JobResult
{
    /// <summary>Whether the run succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>HTTP status for HTTP executors; 0 otherwise.</summary>
    public int StatusCode { get; init; }
    /// <summary>Elapsed execution time.</summary>
    public int DelayMs { get; init; }
    /// <summary>Failure detail.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Signals that a failed result is transient and may be retried when the job
    /// has a <see cref="ScheduledJob.Retry"/> policy. Defaults to false: executors
    /// opt in per failure instead of masking persistent errors behind retries.
    /// </summary>
    public bool Retryable { get; init; }

    /// <summary>Executor-provided metadata.</summary>
    public Dictionary<string, object?> Metadata { get; init; } = [];
    /// <summary>When the run finished.</summary>
    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.UtcNow;
}
