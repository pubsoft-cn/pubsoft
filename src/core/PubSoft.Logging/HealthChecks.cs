namespace PubSoft.Logging;

/// <summary>健康状态</summary>
public enum HealthStatus : byte
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2
}

/// <summary>健康检查结果</summary>
public readonly record struct HealthCheckResult(HealthStatus Status, string? Description = null)
{
    public static HealthCheckResult Healthy(string? desc = null) => new(HealthStatus.Healthy, desc);
    public static HealthCheckResult Degraded(string desc) => new(HealthStatus.Degraded, desc);
    public static HealthCheckResult Unhealthy(string desc) => new(HealthStatus.Unhealthy, desc);
}

/// <summary>健康检查契约</summary>
public interface IAppHealthCheck
{
    string Name { get; }
    ValueTask<HealthCheckResult> CheckAsync(CancellationToken ct = default);
}

/// <summary>
/// 健康检查聚合器 · 汇总所有 IAppHealthCheck 结果，计算整体状态。
/// <para>用于 K8s 就绪/存活探针、监控仪表盘。</para>
/// </summary>
public sealed class HealthCheckAggregator(IEnumerable<IAppHealthCheck> checks)
{
    /// <summary>执行所有健康检查</summary>
    public async Task<IReadOnlyList<(string Name, HealthCheckResult Result)>> CheckAllAsync(CancellationToken ct = default)
    {
        var results = new List<(string, HealthCheckResult)>();
        foreach (var check in checks)
        {
            try
            {
                results.Add((check.Name, await check.CheckAsync(ct).ConfigureAwait(false)));
            }
            catch (Exception ex)
            {
                results.Add((check.Name, HealthCheckResult.Unhealthy(ex.Message)));
            }
        }
        return results;
    }

    /// <summary>获取整体健康状态</summary>
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
