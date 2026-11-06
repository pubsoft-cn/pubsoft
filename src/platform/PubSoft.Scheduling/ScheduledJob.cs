using System.Text.Json;

namespace PubSoft.Scheduling;


[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
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
