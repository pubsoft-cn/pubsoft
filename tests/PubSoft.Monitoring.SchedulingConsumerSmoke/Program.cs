using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;
using PubSoft.Monitoring.Scheduling;
using PubSoft.Notifications;
using PubSoft.Scheduling;

var services = new ServiceCollection();
var channel = new RecordingChannel();
services.AddSingleton<INotificationChannel>(channel);
services.AddSingleton<IProbeHealthCheck, HealthyProbe>();
services.AddPubSoftMonitoring();
services.AddPubSoftNotifications();
services.AddSingleton<IStateStore, MemoryStateStore>();
services.AddPubSoftMonitoringScheduling(options => options.FailureThreshold = 2);
using var provider = services.BuildServiceProvider();

var processor = provider.GetServices<IResultProcessor>().Single(resultProcessor => resultProcessor.Name == "AlertRules");
if (processor is not AlertRuleProcessor)
    throw new InvalidOperationException("Monitoring scheduler package consumer check failed.");

var job = new ScheduledJob
{
    Name = "consumer-monitoring",
    ResultProcessorConfig = JsonSerializer.SerializeToElement(new AlertRuleConfig
    {
        Rules =
        [
            new AlertRule
            {
                Name = "confirmed",
                Condition = AlertConditionType.StateConfirmed,
                Channels = ["consumer"]
            },
            new AlertRule
            {
                Name = "recovered",
                Condition = AlertConditionType.StateRecovered,
                Channels = ["consumer"]
            }
        ]
    })
};
await processor.ProcessAsync(job, new JobResult { Success = false, StatusCode = 503 }, default);
if (channel.Messages.Count != 0)
    throw new InvalidOperationException("Alert threshold emitted before confirmation.");

await processor.ProcessAsync(job, new JobResult { Success = false, StatusCode = 503 }, default);
await processor.ProcessAsync(job, new JobResult { Success = true, StatusCode = 200 }, default);
if (channel.Messages.Count != 2
    || !channel.Messages[0].Title!.Contains("confirmed", StringComparison.OrdinalIgnoreCase)
    || !channel.Messages[1].Title!.Contains("recovered", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Monitoring alert transition consumer check failed.");

Console.WriteLine("Isolated PubSoft.Monitoring.Scheduling alert runtime check passed.");

sealed class HealthyProbe : IProbeHealthCheck
{
    public Task<ProbeHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProbeHealth.Healthy);
    }
}

sealed class RecordingChannel : INotificationChannel
{
    public string Name => "consumer";
    public List<NotificationMessage> Messages { get; } = [];

    public Task<NotificationResult> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Messages.Add(message);
        return Task.FromResult(NotificationResult.Ok());
    }
}