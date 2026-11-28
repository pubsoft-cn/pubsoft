using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;
using PubSoft.Monitoring.OpenTelemetry;

var services = new ServiceCollection();
services.AddPubSoftOpenTelemetry(options =>
{
    options.ServiceName = "consumer-smoke";
    options.AdditionalMeters.Add("Consumer.Additional");
    options.AdditionalActivitySources.Add("Consumer.Source");
});
using var provider = services.BuildServiceProvider();
provider.GetRequiredService<MonitoringMetrics>().RecordRequest(200);

Console.WriteLine("Isolated PubSoft.Monitoring.OpenTelemetry consumer smoke check passed.");
