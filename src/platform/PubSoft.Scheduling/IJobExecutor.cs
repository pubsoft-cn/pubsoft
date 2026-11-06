using System.Text.Json;

namespace PubSoft.Scheduling;

public interface IJobExecutor
{
    string Type { get; }
    Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken);
}
