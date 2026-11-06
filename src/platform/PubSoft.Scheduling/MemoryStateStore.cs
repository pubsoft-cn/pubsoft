using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


/// <summary>In-process state store - last results + custom keys. Per-process; not shared across replicas.</summary>
public sealed class MemoryStateStore : IStateStore
{
    private static readonly TimeSpan s_lastResultLifetime = TimeSpan.FromDays(7);
    private readonly ConcurrentDictionary<string, StoredValue> _values = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ILogger<MemoryStateStore>? _logger;

    /// <summary>Creates the store.</summary>
    public MemoryStateStore(TimeProvider? timeProvider = null, ILogger<MemoryStateStore>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        return GetAsync<JobResult>($"last:{jobName}", cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateStateAsync(string jobName, JobResult result, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(result);
        return SetAsync($"last:{jobName}", result, s_lastResultLifetime, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T?> GetCustomStateAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return GetAsync<T>($"custom:{key}", cancellationToken);
    }

    /// <inheritdoc />
    public Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (expiry <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(expiry));
        return SetAsync($"custom:{key}", value, expiry, cancellationToken);
    }

    /// <inheritdoc />
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
