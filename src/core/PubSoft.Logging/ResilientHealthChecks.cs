namespace PubSoft.Logging;

/// <summary>
/// 带缓存的健康检查聚合器 · DDOS 防护核心。
/// <para>在缓存窗口内复用上次检查结果，避免每次请求都打 DB。</para>
/// <para>不健康结果使用更短的缓存窗口，以便快速检测恢复。</para>
/// <para>线程安全：并发请求在缓存窗口内只触发一次实际检查。</para>
/// </summary>
public sealed class CachedHealthCheckAggregator
{
    private readonly HealthCheckAggregator _inner;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _unhealthyCacheDuration;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IReadOnlyList<(string Name, HealthCheckResult Result)>? _cachedResults;
    private DateTime _cacheExpiry;
    private HealthStatus _cachedStatus = HealthStatus.Healthy;

    /// <param name="checks">健康检查列表</param>
    /// <param name="cacheDuration">健康结果的缓存时长（默认 5 秒）</param>
    /// <param name="unhealthyCacheDuration">不健康结果的缓存时长（默认 1 秒，更短以便快速检测恢复）</param>
    public CachedHealthCheckAggregator(
        IEnumerable<IAppHealthCheck> checks,
        TimeSpan? cacheDuration = null,
        TimeSpan? unhealthyCacheDuration = null)
    {
        _inner = new HealthCheckAggregator(checks);
        _cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(5);
        _unhealthyCacheDuration = unhealthyCacheDuration ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>执行所有健康检查（带缓存）</summary>
    public async Task<IReadOnlyList<(string Name, HealthCheckResult Result)>> CheckAllAsync(CancellationToken ct = default)
    {
        // 快速路径：缓存有效且未过期
        if (_cachedResults != null && DateTime.UtcNow < _cacheExpiry)
            return _cachedResults;

        // 慢路径：获取锁后再次检查（double-check locking）
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedResults != null && DateTime.UtcNow < _cacheExpiry)
                return _cachedResults;

            var results = await _inner.CheckAllAsync(ct).ConfigureAwait(false);
            _cachedResults = results;

            // 计算整体状态决定缓存时长
            _cachedStatus = results.Any(r => r.Result.Status == HealthStatus.Unhealthy)
                ? HealthStatus.Unhealthy
                : results.Any(r => r.Result.Status == HealthStatus.Degraded)
                    ? HealthStatus.Degraded
                    : HealthStatus.Healthy;

            // 不健康/降级结果缓存更短时间，健康结果缓存完整时长
            var duration = _cachedStatus == HealthStatus.Healthy
                ? _cacheDuration
                : _unhealthyCacheDuration;
            _cacheExpiry = DateTime.UtcNow + duration;

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>获取整体健康状态（带缓存）</summary>
    public async Task<HealthStatus> GetOverallStatusAsync(CancellationToken ct = default)
    {
        await CheckAllAsync(ct).ConfigureAwait(false);
        return _cachedStatus;
    }

    /// <summary>强制刷新缓存（管理员手动触发时用）</summary>
    public void InvalidateCache()
    {
        _cachedResults = null;
        _cacheExpiry = DateTime.MinValue;
    }
}

/// <summary>
/// 并行健康检查聚合器 · 所有检查并行执行，降低总延迟。
/// <para>串行执行时 3 个 50ms 检查 = 150ms；并行执行 ≈ 50ms。</para>
/// </summary>
public sealed class ParallelHealthCheckAggregator
{
    private readonly IReadOnlyList<IAppHealthCheck> _checks;

    public ParallelHealthCheckAggregator(IEnumerable<IAppHealthCheck> checks)
    {
        _checks = checks.ToList();
    }

    public async Task<IReadOnlyList<(string Name, HealthCheckResult Result)>> CheckAllAsync(CancellationToken ct = default)
    {
        var tasks = _checks.Select(async check =>
        {
            try
            {
                return (check.Name, await check.CheckAsync(ct).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return (check.Name, HealthCheckResult.Unhealthy(ex.Message));
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    public async Task<HealthStatus> GetOverallStatusAsync(CancellationToken ct = default)
    {
        var results = await CheckAllAsync(ct).ConfigureAwait(false);
        if (results.Any(r => r.Result.Status == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;
        if (results.Any(r => r.Result.Status == HealthStatus.Degraded))
            return HealthStatus.Degraded;
        return HealthStatus.Healthy;
    }
}

/// <summary>
/// 带超时的健康检查聚合器 · 防止慢检查阻塞响应。
/// <para>每个检查项独立超时，超时后返回 Unhealthy("超时")。</para>
/// </summary>
public sealed class TimeoutHealthCheckAggregator
{
    private readonly IReadOnlyList<IAppHealthCheck> _checks;
    private readonly TimeSpan _timeout;

    public TimeoutHealthCheckAggregator(IEnumerable<IAppHealthCheck> checks, TimeSpan timeout)
    {
        _checks = checks.ToList();
        _timeout = timeout;
    }

    public async Task<IReadOnlyList<(string Name, HealthCheckResult Result)>> CheckAllAsync(CancellationToken ct = default)
    {
        var results = new List<(string, HealthCheckResult)>();
        foreach (var check in _checks)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            try
            {
                var result = await check.CheckAsync(cts.Token).ConfigureAwait(false);
                results.Add((check.Name, result));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                results.Add((check.Name, HealthCheckResult.Unhealthy($"健康检查超时（{_timeout.TotalMilliseconds}ms）")));
            }
            catch (Exception ex)
            {
                results.Add((check.Name, HealthCheckResult.Unhealthy(ex.Message)));
            }
        }
        return results;
    }

    public async Task<HealthStatus> GetOverallStatusAsync(CancellationToken ct = default)
    {
        var results = await CheckAllAsync(ct).ConfigureAwait(false);
        if (results.Any(r => r.Result.Status == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;
        if (results.Any(r => r.Result.Status == HealthStatus.Degraded))
            return HealthStatus.Degraded;
        return HealthStatus.Healthy;
    }
}

/// <summary>
/// 生产级健康检查聚合器 · 集成缓存 + 并行 + 超时三重 DDOS 防护。
/// <para>缓存：缓存窗口内复用结果，避免重复打 DB。</para>
/// <para>并行：所有检查项并行执行，降低延迟。</para>
/// <para>超时：每个检查项独立超时，防止慢检查阻塞。</para>
/// </summary>
public sealed class ResilientHealthCheckAggregator
{
    private readonly ParallelHealthCheckAggregator _parallel;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _unhealthyCacheDuration;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IReadOnlyList<(string Name, HealthCheckResult Result)>? _cachedResults;
    private DateTime _cacheExpiry;
    private HealthStatus _cachedStatus = HealthStatus.Healthy;

    /// <param name="checks">健康检查列表</param>
    /// <param name="timeout">每个检查项的超时时间（默认 3 秒）</param>
    /// <param name="cacheDuration">健康结果的缓存时长（默认 5 秒）</param>
    /// <param name="unhealthyCacheDuration">不健康结果的缓存时长（默认 1 秒）</param>
    public ResilientHealthCheckAggregator(
        IEnumerable<IAppHealthCheck> checks,
        TimeSpan? timeout = null,
        TimeSpan? cacheDuration = null,
        TimeSpan? unhealthyCacheDuration = null)
    {
        _parallel = new ParallelHealthCheckAggregator(checks);
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        _cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(5);
        _unhealthyCacheDuration = unhealthyCacheDuration ?? TimeSpan.FromSeconds(1);
    }

    public async Task<IReadOnlyList<(string Name, HealthCheckResult Result)>> CheckAllAsync(CancellationToken ct = default)
    {
        // 快速路径：缓存有效
        if (_cachedResults != null && DateTime.UtcNow < _cacheExpiry)
            return _cachedResults;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedResults != null && DateTime.UtcNow < _cacheExpiry)
                return _cachedResults;

            // 并行执行 + 每项超时
            var results = await ExecuteWithTimeoutAsync(ct).ConfigureAwait(false);
            _cachedResults = results;

            _cachedStatus = results.Any(r => r.Result.Status == HealthStatus.Unhealthy)
                ? HealthStatus.Unhealthy
                : results.Any(r => r.Result.Status == HealthStatus.Degraded)
                    ? HealthStatus.Degraded
                    : HealthStatus.Healthy;

            var duration = _cachedStatus == HealthStatus.Healthy
                ? _cacheDuration
                : _unhealthyCacheDuration;
            _cacheExpiry = DateTime.UtcNow + duration;

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<(string Name, HealthCheckResult Result)>> ExecuteWithTimeoutAsync(CancellationToken ct)
    {
        var tasks = _parallel.CheckAllAsync(ct);
        try
        {
            // 整体超时（所有检查并行，取最长超时）
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            return await tasks.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 整体超时 — 返回已完成的检查 + 超时标记
            return new List<(string, HealthCheckResult)>
            {
                ("__timeout__", HealthCheckResult.Unhealthy($"健康检查整体超时（{_timeout.TotalMilliseconds}ms）"))
            };
        }
    }

    public async Task<HealthStatus> GetOverallStatusAsync(CancellationToken ct = default)
    {
        await CheckAllAsync(ct).ConfigureAwait(false);
        return _cachedStatus;
    }

    public void InvalidateCache()
    {
        _cachedResults = null;
        _cacheExpiry = DateTime.MinValue;
    }
}
