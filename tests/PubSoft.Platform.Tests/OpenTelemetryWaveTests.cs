using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;
using PubSoft.Monitoring.OpenTelemetry;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OpenTelemetryWaveTests
{
    [Fact]
    public void RegistrationRequiresServiceNameAndSafeOtlpEndpoint()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPubSoftOpenTelemetry(_ => { }));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPubSoftOpenTelemetry(options =>
        {
            options.ServiceName = "demo-app";
            options.OtlpEndpoint = new Uri("grpc://collector:4317");
        }));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPubSoftOpenTelemetry(options =>
        {
            options.ServiceName = "demo-app";
            options.AdditionalMeters = ["a", "a"];
        }));
    }

    [Fact]
    public void RegistrationWiresPubSoftMetricsAndOptionalInstrumentationWithoutExporterByDefault()
    {
        var services = new ServiceCollection();
        services.AddPubSoftOpenTelemetry(options =>
        {
            options.ServiceName = "demo-app";
            options.ServiceVersion = "1.0.0";
            options.IncludeAspNetCoreTraceInstrumentation = true;
            options.IncludeHttpClientTraceInstrumentation = true;
            options.AdditionalMeters.Add("Demo.Meter");
            options.AdditionalActivitySources.Add("Demo.Source");
        });

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<MonitoringMetrics>());
    }

    [Fact]
    public void AbsoluteHttpOtlpEndpointIsAcceptedAsExplicitExportOptIn()
    {
        var services = new ServiceCollection();
        services.AddPubSoftOpenTelemetry(options =>
        {
            options.ServiceName = "demo-app";
            options.OtlpEndpoint = new Uri("http://localhost:4317");
        });

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<MonitoringMetrics>());
    }
}
