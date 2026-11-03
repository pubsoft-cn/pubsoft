using System.Text.Json;

namespace PubSoft.Scheduling;

public sealed class ScheduledJob
{
    public string Name { get; set; } = "";
    public ScheduleMask Schedule { get; set; } = ScheduleMask.EverySecond;
    public string ExecutorType { get; set; } = "Http";
    public JsonElement ExecutorConfig { get; set; }
    public List<string> ResultProcessors { get; set; } = [];
    public JsonElement ResultProcessorConfig { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Skips a due run while a previous run of the same job is still executing.
    /// Defaults to true: overlapping runs of one job are almost never intended.
    /// <para>Process-local only: multiple scheduler instances sharing state can run
    /// the same job concurrently — distributed mutual exclusion is not provided.</para>
    /// </summary>
    public bool PreventOverlap { get; set; } = true;

    /// <summary>Per-attempt timeout; null means the job runs until completion or host shutdown.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Retry policy applied to retryable failures and thrown exceptions; null disables retries.</summary>
    public JobRetryPolicy? Retry { get; set; }
}

/// <summary>Bounded exponential-backoff retry for a <see cref="ScheduledJob"/>.</summary>
public sealed class JobRetryPolicy
{
    /// <summary>Total attempts including the first execution (1 = no retry).</summary>
    public int MaxAttempts { get; set; } = 1;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    public double BackoffFactor { get; set; } = 2.0;
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);
}

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

public interface IJobExecutor
{
    string Type { get; }
    Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken);
}

public interface IResultProcessor
{
    string Name { get; }
    Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken cancellationToken);
}

public interface IStateStore
{
    Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken cancellationToken);
    Task UpdateStateAsync(string jobName, JobResult result, CancellationToken cancellationToken);
    Task<T?> GetCustomStateAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken) where T : class;
    Task RemoveCustomStateAsync(string key, CancellationToken cancellationToken);
}