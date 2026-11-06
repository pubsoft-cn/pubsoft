using System.Text.Json;

namespace PubSoft.Scheduling;


/// <summary>Post-run hook applied to each job result.</summary>
public interface IResultProcessor
{
    /// <summary>Registration name.</summary>
    string Name { get; }
    /// <summary>Processes a finished run.</summary>
    Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken cancellationToken);
}
