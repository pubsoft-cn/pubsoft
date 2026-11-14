using Microsoft.Extensions.DependencyInjection;
using PubSoft.Outbound;
using PubSoft.Scheduling;
using Xunit;

namespace PubSoft.Platform.Tests;

/// <summary>Health-check + remaining registration smoke coverage.</summary>
public sealed class HealthCheckWaveTests
{
    [Fact]
    public void SchedulerRegisters()
    {
        var services = new ServiceCollection().AddPubSoftScheduler();
        Assert.NotEmpty(services);
        Assert.Contains(services, d => d.ServiceType.FullName!.Contains("Scheduler"));
    }

    [Fact]
    public void OutboundSdkRegisters()
    {
        var services = new ServiceCollection().AddExternalOutboundSdk();
        Assert.NotEmpty(services);
    }
}
