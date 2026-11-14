using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


/// <summary>Built-in result processor: logs each run outcome.</summary>
public sealed class LoggingResultProcessor : IResultProcessor
{
    private readonly ILogger<LoggingResultProcessor>? _logger;

    /// <summary>Creates the processor.</summary>
    public LoggingResultProcessor(ILogger<LoggingResultProcessor>? logger = null) => _logger = logger;

    /// <inheritdoc />
    public string Name => "logging";

    /// <inheritdoc />
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
