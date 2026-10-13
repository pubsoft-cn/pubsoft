namespace PubSoft.Monitoring;

public enum AppHealthStatus : byte
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2
}

public readonly record struct AppHealthCheckResult(AppHealthStatus Status, string? Description = null)
{
    public static AppHealthCheckResult Healthy(string? description = null)
        => new(AppHealthStatus.Healthy, description);

    public static AppHealthCheckResult Degraded(string description)
        => new(AppHealthStatus.Degraded, description);

    public static AppHealthCheckResult Unhealthy(string description)
        => new(AppHealthStatus.Unhealthy, description);
}

public interface IAppHealthCheck
{
    string Name { get; }
    ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class HealthCheckAggregator
{
    private readonly IReadOnlyList<IAppHealthCheck> _checks;

    public HealthCheckAggregator(IEnumerable<IAppHealthCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        _checks = checks.ToArray();
        if (_checks.Any(check => check is null))
            throw new ArgumentException("Health checks cannot contain null entries.", nameof(checks));
    }

    public async Task<IReadOnlyList<(string Name, AppHealthCheckResult Result)>> CheckAllAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<(string Name, AppHealthCheckResult Result)>(_checks.Count);
        foreach (var check in _checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add((check.Name, await check.CheckAsync(cancellationToken).ConfigureAwait(false)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                results.Add((check.Name, AppHealthCheckResult.Unhealthy("Health check failed.")));
            }
        }

        return results.AsReadOnly();
    }

    public async Task<AppHealthStatus> GetOverallStatusAsync(CancellationToken cancellationToken = default)
    {
        var results = await CheckAllAsync(cancellationToken).ConfigureAwait(false);
        if (results.Any(result => result.Result.Status == AppHealthStatus.Unhealthy))
            return AppHealthStatus.Unhealthy;
        if (results.Any(result => result.Result.Status == AppHealthStatus.Degraded))
            return AppHealthStatus.Degraded;
        return AppHealthStatus.Healthy;
    }
}