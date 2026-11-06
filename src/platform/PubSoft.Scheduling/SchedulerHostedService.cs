using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Scheduling;

public sealed class SchedulerHostedService : BackgroundService
{
    private readonly SchedulerEngine _engine;
    private readonly IOptionsMonitor<SchedulerOptions> _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ILogger<SchedulerHostedService>? _logger;

    public SchedulerHostedService(
        SchedulerEngine engine,
        IOptionsMonitor<SchedulerOptions> options,
        TimeProvider timeProvider,
        ILogger<SchedulerHostedService>? logger = null)
    {
        _engine = engine;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger?.LogInformation("Scheduler started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            SchedulerOptions? options = null;
            try
            {
                options = _options.CurrentValue;
                await _engine.RunDueJobsAsync(options, _timeProvider.GetUtcNow(), stoppingToken).ConfigureAwait(false);
                await Task.Delay(options.LoopInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "Scheduler loop failed.");
                await Task.Delay(options?.ErrorDelay ?? TimeSpan.FromSeconds(5), _timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        _logger?.LogInformation("Scheduler stopped.");
    }
}