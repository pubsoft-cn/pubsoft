using System.Text.Json;

namespace PubSoft.Scheduling;


public interface IResultProcessor
{
    string Name { get; }
    Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken cancellationToken);
}
