using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;

public interface IExecutorRegistry
{
    IJobExecutor? Resolve(string executorType);
    void Register(IJobExecutor executor);
}

public interface IResultProcessorRegistry
{
    IResultProcessor? Resolve(string name);
    void Register(IResultProcessor processor);
}

public sealed class ExecutorRegistry : IExecutorRegistry
{
    private readonly ConcurrentDictionary<string, IJobExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);

    public ExecutorRegistry(IEnumerable<IJobExecutor> executors)
    {
        foreach (var executor in executors)
            Register(executor);
    }

    public IJobExecutor? Resolve(string executorType)
        => _executors.TryGetValue(executorType, out var executor) ? executor : null;

    public void Register(IJobExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentException.ThrowIfNullOrWhiteSpace(executor.Type);
        _executors[executor.Type] = executor;
    }
}

public sealed class ResultProcessorRegistry : IResultProcessorRegistry
{
    private readonly ConcurrentDictionary<string, IResultProcessor> _processors = new(StringComparer.OrdinalIgnoreCase);

    public ResultProcessorRegistry(IEnumerable<IResultProcessor> processors)
    {
        foreach (var processor in processors)
            Register(processor);
    }

    public IResultProcessor? Resolve(string name)
        => _processors.TryGetValue(name, out var processor) ? processor : null;

    public void Register(IResultProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentException.ThrowIfNullOrWhiteSpace(processor.Name);
        _processors[processor.Name] = processor;
    }
}

public sealed class MemoryStateStore : IStateStore
{
    private static readonly TimeSpan s_lastResultLifetime = TimeSpan.FromDays(7);
    private readonly ConcurrentDictionary<string, StoredValue> _values = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemoryStateStore>? _logger;

    public MemoryStateStore(TimeProvider? timeProvider = null, ILogger<MemoryStateStore>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        return GetAsync<JobResult>($"last:{jobName}", cancellationToken);
    }

    public Task UpdateStateAsync(string jobName, JobResult result, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(result);
        return SetAsync($"last:{jobName}", result, s_lastResultLifetime, cancellationToken);
    }

    public Task<T?> GetCustomStateAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return GetAsync<T>($"custom:{key}", cancellationToken);
    }

    public Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (expiry <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(expiry));
        return SetAsync($"custom:{key}", value, expiry, cancellationToken);
    }

    public Task RemoveCustomStateAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        _values.TryRemove($"custom:{key}", out _);
        return Task.CompletedTask;
    }

    private Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_values.TryGetValue(key, out var entry))
            return Task.FromResult<T?>(null);

        if (entry.ExpiresAt is { } expiresAt && expiresAt <= _timeProvider.GetUtcNow())
        {
            _values.TryRemove(key, out _);
            _logger?.LogDebug("Expired scheduler state entry removed.");
            return Task.FromResult<T?>(null);
        }

        return Task.FromResult(entry.Value as T);
    }

    private Task SetAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset? expiresAt = expiry is { } lifetime ? _timeProvider.GetUtcNow() + lifetime : null;
        _values[key] = new StoredValue(value, expiresAt);
        return Task.CompletedTask;
    }

    private sealed record StoredValue(object Value, DateTimeOffset? ExpiresAt);
}

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