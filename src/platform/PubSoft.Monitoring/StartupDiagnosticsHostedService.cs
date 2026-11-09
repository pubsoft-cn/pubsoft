using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PubSoft.Monitoring;

/// <summary>Writes every collected posture entry to the log once at startup -- warnings are visible
/// even when no diagnostics endpoint is mapped.</summary>
public sealed class StartupDiagnosticsHostedService(
    StartupDiagnosticsCollector collector,
    ILogger<StartupDiagnosticsHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in collector.Collect())
        {
            switch (entry.Status)
            {
                case StartupDiagnosticsStatus.Error:
                    logger.LogError("[startup:{Area}] {Detail}", entry.Area, entry.Detail);
                    break;
                case StartupDiagnosticsStatus.Warning:
                    logger.LogWarning("[startup:{Area}] {Detail}", entry.Area, entry.Detail);
                    break;
                default:
                    logger.LogInformation("[startup:{Area}] {Detail}", entry.Area, entry.Detail);
                    break;
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
