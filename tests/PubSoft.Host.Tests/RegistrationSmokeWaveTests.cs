using Microsoft.Extensions.DependencyInjection;
using PubSoft.Core;
using PubSoft.AspNetCore.Auditing;
using PubSoft.AspNetCore.Authentication;
using PubSoft.Monitoring;
using PubSoft.Auditing;
using PubSoft.Authentication;
using PubSoft.DependencyInjection;
using Xunit;

namespace PubSoft.Host.Tests;

/// <summary>B-layer smoke: registration extensions resolve their core services.</summary>
public sealed class RegistrationSmokeWaveTests
{
    [Fact]
    public void IdGeneratorRegisters()
    {
        var sp = new ServiceCollection().AddPubSoftIdGenerator(7).BuildServiceProvider();
        Assert.NotNull(sp.GetService<IIdGenerator>());
        Assert.NotEqual(0, sp.GetRequiredService<IIdGenerator>().NextId());
    }

    [Fact]
    public void AuditActorRegisters()
    {
        var services = new ServiceCollection().AddCurrentSubjectAuditActor();
        Assert.Contains(services, d => d.ServiceType == typeof(IAuditActorAccessor)); // resolves only when the host also registers ICurrentSubjectAccessor
    }

    [Fact]
    public void PrincipalSnapshotRegisters()
    {
        var sp = new ServiceCollection().AddPrincipalSnapshotAccessor().BuildServiceProvider();
        Assert.NotNull(sp.GetService<IPrincipalSnapshotAccessor>());
    }

    [Fact]
    public void DiagnosticsHostedServiceRegisters()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddPubSoftStartupDiagnosticsHostedService()
            .BuildServiceProvider();
        Assert.Contains(sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            h => h is StartupDiagnosticsHostedService);
    }
}
