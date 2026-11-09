namespace PubSoft.Monitoring;

/// <summary>Health aggregation with separate healthy/unhealthy cache TTLs, single-flight refresh.</summary>
public sealed class CachedHealthCheckAggregator
{
    private readonly HealthCheckAggregator _inner;
    private readonly TimeSpan _healthyCacheDuration;
    private readonly TimeSpan _unhealthyCacheDuration;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _cacheLock = new();
    private CacheSnapshot? _cached;
    private long _generation;

    /// <summary>Creates the aggregator.</summary>
    public CachedHealthCheckAggregator(
        IEnumerable<IAppHealthCheck> checks,
        TimeSpan? healthyCacheDuration = null,
        TimeSpan? unhealthyCacheDuration = null,
        TimeProvider? timeProvider = null)
    {
        _inner = new HealthCheckAggregator(checks);
        _healthyCacheDuration = healthyCacheDuration ?? TimeSpan.FromSeconds(5);
        _unhealthyCacheDuration = unhealthyCacheDuration ?? TimeSpan.FromSeconds(1);
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_healthyCacheDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(healthyCacheDuration));
        if (_unhealthyCacheDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(unhealthyCacheDuration));
    }

    /// <summary>Runs all checks, serving cache within TTL.</summary>
    public async Task<IReadOnlyList<(string Name, AppHealthCheckResult Result)>> CheckAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (TryGetCached(out var cached))
            return cached.Results;

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCached(out cached))
                return cached.Results;

            long generation;
            lock (_cacheLock)
                generation = _generation;

            var results = await _inner.CheckAllAsync(cancellationToken).ConfigureAwait(false);
            var status = GetOverallStatus(results);
            var duration = status == AppHealthStatus.Healthy
                ? _healthyCacheDuration
                : _unhealthyCacheDuration;
            var snapshot = new CacheSnapshot(results, _timeProvider.GetUtcNow() + duration);

            lock (_cacheLock)
            {
                if (_generation == generation)
                    _cached = snapshot;
            }

            return results;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Aggregated worst-case status.</summary>
    public async Task<AppHealthStatus> GetOverallStatusAsync(CancellationToken cancellationToken = default)
    {
        var results = await CheckAllAsync(cancellationToken).ConfigureAwait(false);
        return GetOverallStatus(results);
    }

    /// <summary>Drops the cached snapshot so the next call refreshes.</summary>
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _generation++;
            _cached = null;
        }
    }

    private bool TryGetCached(out CacheSnapshot cached)
    {
        lock (_cacheLock)
        {
            if (_cached is { } snapshot && _timeProvider.GetUtcNow() < snapshot.ExpiresAt)
            {
                cached = snapshot;
                return true;
            }
        }

        cached = null!; // pubsoft:exempt PUBSOFT046 "out-param reset on the failure path"
        return false;
    }

    private static AppHealthStatus GetOverallStatus(
        IReadOnlyList<(string Name, AppHealthCheckResult Result)> results)
    {
        if (results.Any(result => result.Result.Status == AppHealthStatus.Unhealthy))
            return AppHealthStatus.Unhealthy;
        if (results.Any(result => result.Result.Status == AppHealthStatus.Degraded))
            return AppHealthStatus.Degraded;
        return AppHealthStatus.Healthy;
    }

    private sealed record CacheSnapshot(
        IReadOnlyList<(string Name, AppHealthCheckResult Result)> Results,
        DateTimeOffset ExpiresAt);
}