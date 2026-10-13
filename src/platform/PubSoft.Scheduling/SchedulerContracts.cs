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
}

public sealed class JobResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public int DelayMs { get; init; }
    public string? ErrorMessage { get; init; }
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