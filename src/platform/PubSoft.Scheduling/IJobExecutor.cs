namespace PubSoft.Scheduling;

/// <summary>Runs one scheduled job type.</summary>
public interface IJobExecutor
{
    /// <summary>Executor type name matched against <see cref="ScheduledJob.ExecutorType"/>.</summary>
    string Type { get; }
    /// <summary>Runs the job.</summary>
    /// <param name="job">Static job configuration.</param>
    /// <param name="executionContext">Runtime occurrence identity, misfire marker, and fencing token for this execution.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    Task<JobResult> ExecuteAsync(
        ScheduledJob job,
        JobExecutionContext executionContext,
        CancellationToken cancellationToken);
}
