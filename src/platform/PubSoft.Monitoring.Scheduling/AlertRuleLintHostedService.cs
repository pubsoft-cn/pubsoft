using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;

/// <summary>
/// Startup lint: validates the <c>AlertRules</c> configuration of every job that opts into the
/// alert processor, so a bad rule set surfaces at deploy time instead of the job's first execution.
/// Registered automatically by <see cref="MonitoringSchedulingServiceCollectionExtensions.AddPubSoftMonitoringScheduling"/>.
/// </summary>
public sealed class AlertRuleLintHostedService : IHostedService
{
    private readonly IOptions<SchedulerOptions> _options;
    private readonly ILogger<AlertRuleLintHostedService>? _logger;

    /// <summary>Creates the lint service.</summary>
    public AlertRuleLintHostedService(IOptions<SchedulerOptions> options, ILogger<AlertRuleLintHostedService>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var job in _options.Value.Jobs)
        {
            if (job.ResultProcessors is null
                || !job.ResultProcessors.Contains(AlertRuleProcessor.ProcessorName, StringComparer.Ordinal))
                continue;
            AlertRuleProcessor.TryReadAlertRules(job.ResultProcessorConfig, _logger, out _, job.Name);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
