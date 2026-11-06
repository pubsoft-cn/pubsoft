namespace PubSoft.Monitoring;


/// <summary>Runs health checks sequentially; a throwing check reports Unhealthy, never crashes the aggregate.</summary>
public sealed class HealthCheckAggregator
{
    private readonly IReadOnlyList<IAppHealthCheck> _checks;

    /// <summary>Creates the aggregator.</summary>
    public HealthCheckAggregator(IEnumerable<IAppHealthCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        _checks = checks.ToArray();
        if (_checks.Any(check => check is null))
            throw new ArgumentException("Health checks cannot contain null entries.", nameof(checks));
    }

    /// <summary>Runs all checks sequentially.</summary>
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

    /// <summary>Aggregated worst-case status.</summary>
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
