namespace PubSoft.Monitoring;

public sealed class ParallelHealthCheckAggregator
{
    private readonly IReadOnlyList<IAppHealthCheck> _checks;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _concurrencyGate;

    public ParallelHealthCheckAggregator(
        IEnumerable<IAppHealthCheck> checks,
        TimeSpan? timeout = null,
        int maxConcurrency = 8)
    {
        ArgumentNullException.ThrowIfNull(checks);
        _checks = checks.ToArray();
        if (_checks.Any(check => check is null))
            throw new ArgumentException("Health checks cannot contain null entries.", nameof(checks));

        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        _concurrencyGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public async Task<IReadOnlyList<(string Name, AppHealthCheckResult Result)>> CheckAllAsync(
        CancellationToken cancellationToken = default)
    {
        var tasks = _checks.Select(check => ExecuteCheckAsync(check, cancellationToken)).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
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

    private async Task<(string Name, AppHealthCheckResult Result)> ExecuteCheckAsync(
        IAppHealthCheck check,
        CancellationToken cancellationToken)
    {
        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        var timeoutToken = timeoutSource.Token;
        Task<AppHealthCheckResult> checkTask;
        try
        {
            checkTask = check.CheckAsync(timeoutSource.Token).AsTask();
        }
        catch (Exception exception)
        {
            timeoutSource.Dispose();
            _concurrencyGate.Release();
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;
            return (check.Name, AppHealthCheckResult.Unhealthy("Health check failed."));
        }

        _ = ReleaseConcurrencyWhenCompletedAsync(checkTask, timeoutSource);
        try
        {
            var result = await checkTask.WaitAsync(timeoutToken).ConfigureAwait(false);
            return (check.Name, result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutToken.IsCancellationRequested)
        {
            return (check.Name, AppHealthCheckResult.Unhealthy("Health check timed out."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return (check.Name, AppHealthCheckResult.Unhealthy("Health check failed."));
        }
    }

    private async Task ReleaseConcurrencyWhenCompletedAsync(
        Task checkTask,
        CancellationTokenSource timeoutSource)
    {
        try
        {
            await checkTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            timeoutSource.Dispose();
            _concurrencyGate.Release();
        }
    }
}