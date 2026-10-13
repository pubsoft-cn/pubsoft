using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;

var services = new ServiceCollection();
services.AddPubSoftMonitoring();
services.AddPubSoftMonitoringMetrics();
services.AddPubSoftHealthCheck<HealthyApplicationHealthCheck>();
services.AddPubSoftHealthChecks();
services.AddPubSoftCachedHealthChecks();
services.AddPubSoftParallelHealthChecks(maxConcurrency: 2);
using var provider = services.BuildServiceProvider();

var health = await provider.GetRequiredService<IProbeHealthCheck>().CheckAsync();
if (health != ProbeHealth.Healthy)
    throw new InvalidOperationException("Monitoring package consumer check failed.");
var appHealth = await provider.GetRequiredService<HealthCheckAggregator>().GetOverallStatusAsync();
if (appHealth != AppHealthStatus.Healthy)
    throw new InvalidOperationException("Application health aggregation consumer check failed.");
var cachedHealth = await provider.GetRequiredService<CachedHealthCheckAggregator>().GetOverallStatusAsync();
if (cachedHealth != AppHealthStatus.Healthy)
    throw new InvalidOperationException("Cached application health consumer check failed.");
var parallelHealth = await provider.GetRequiredService<ParallelHealthCheckAggregator>().GetOverallStatusAsync();
if (parallelHealth != AppHealthStatus.Healthy)
    throw new InvalidOperationException("Parallel application health consumer check failed.");
provider.GetRequiredService<MonitoringMetrics>().RecordRequest(200);

Console.WriteLine("Isolated PubSoft.Monitoring consumer smoke check passed.");

sealed class HealthyApplicationHealthCheck : IAppHealthCheck
{
    public string Name => "consumer";

    public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(AppHealthCheckResult.Healthy());
}