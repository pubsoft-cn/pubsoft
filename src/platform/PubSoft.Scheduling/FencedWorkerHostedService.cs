using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


/// <summary>
/// Periodic worker that runs a delegate under a distributed lock each interval --
/// the "sweep takes the fencing lock first" boilerplate collapsed to one registration.
/// </summary>
/// <remarks>
/// A tick that cannot acquire the lock is skipped (logged at Debug); work exceptions are logged
/// and the loop continues. Cancellation of an over-running tick uses
/// <see cref="FencedWorkerOptions.MaxExecutionTime"/>; the lock handle is released on every exit path.
/// </remarks>
public sealed class FencedWorkerHostedService(
    string workerName,
    FencedWorkerOptions options,
    Func<FencedWorkContext, CancellationToken, Task> work,
    IFencedExecutor executor,
    ILogger<FencedWorkerHostedService>? logger = null) : BackgroundService
{
    private readonly string _workerName = workerName;
    private readonly FencedWorkerOptions _options = options;
    private readonly Func<FencedWorkContext, CancellationToken, Task> _work = work;
    private readonly IFencedExecutor _executor = executor;
    private readonly ILogger<FencedWorkerHostedService>? _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RunOnStartup)
            await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var tickTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            tickTimeout.CancelAfter(_options.MaxExecutionTime);
            try
            {
                var acquired = await _executor.TryExecuteAsync(
                        _options.LockKey ?? _workerName,
                        _options.LockExpiry,
                        _work,
                        _options.WaitTimeout,
                        tickTimeout.Token)
                    .ConfigureAwait(false);
                if (!acquired)
                    _logger?.LogDebug("Fenced worker '{Worker}' skipped a tick: lock is held elsewhere.", _workerName);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning(
                    "Fenced worker '{Worker}' tick exceeded the {Timeout} execution cap and was cancelled.",
                    _workerName, _options.MaxExecutionTime);
            }
            catch (Exception ex) // pubsoft:exempt PUBSOFT045 "worker loop must outlive per-tick failures; logged above"
            {
                _logger?.LogError(ex, "Fenced worker '{Worker}' tick failed; retrying next interval.", _workerName);
            }

            await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
