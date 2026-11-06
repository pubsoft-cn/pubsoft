using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


public sealed class LoggingResultProcessor : IResultProcessor
{
    private readonly ILogger<LoggingResultProcessor>? _logger;

    public LoggingResultProcessor(ILogger<LoggingResultProcessor>? logger = null) => _logger = logger;

    public string Name => "logging";

    public Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Success)
            _logger?.LogInformation("Job {Name} succeeded (status={Status})", job.Name, result.StatusCode);
        else
            _logger?.LogWarning("Job {Name} failed: {Error}", job.Name, result.ErrorMessage);
        return Task.CompletedTask;
    }
}
