using System.Text.Json;

namespace PubSoft.Scheduling;


/// <summary>A schedulable job.</summary>
[PubSoft.Core.BoundedCost("data object - timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public sealed class ScheduledJob
{
    /// <summary>Unique job name (log/lock/state key).</summary>
    public string Name { get; set; } = "";
    /// <summary>When the job is due.</summary>
    public ScheduleMask Schedule { get; set; } = ScheduleMask.EverySecond;
    /// <summary>Executor type resolved via <see cref="IExecutorRegistry"/>.</summary>
    public string ExecutorType { get; set; } = "Http";
    /// <summary>Executor-specific config blob.</summary>
    public JsonElement ExecutorConfig { get; set; }
    /// <summary>Result-processor names applied after each run.</summary>
    public List<string> ResultProcessors { get; set; } = [];
    /// <summary>Result-processor-specific config blob.</summary>
    public JsonElement ResultProcessorConfig { get; set; }
    /// <summary>Disabled jobs are skipped without removing them.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Skips a due run while a previous run of the same job is still executing.
    /// Defaults to true: overlapping runs of one job are almost never intended.
    /// <para>Process-local only: multiple scheduler instances sharing state can run
    /// the same job concurrently -- distributed mutual exclusion is not provided.</para>
    /// </summary>
    public bool PreventOverlap { get; set; } = true;

    /// <summary>Per-attempt timeout; null means the job runs until completion or host shutdown.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Retry policy applied to retryable failures and thrown exceptions; null disables retries.</summary>
    public JobRetryPolicy? Retry { get; set; }
}
