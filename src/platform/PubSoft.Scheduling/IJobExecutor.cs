namespace PubSoft.Scheduling;

/// <summary>Runs one scheduled job type.</summary>
public interface IJobExecutor
{
    /// <summary>Executor type name matched against <see cref="ScheduledJob.ExecutorType"/>.</summary>
    string Type { get; }
    /// <summary>Runs the job.</summary>
    Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken);
}
