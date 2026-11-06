using Microsoft.Extensions.DependencyInjection;
using PubSoft.Authorization;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EfCoreDiWaveTests
{
    private sealed class E : Entity<long> { }
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
    private sealed class Ctx(Microsoft.EntityFrameworkCore.DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant()) { }

    [Fact]
    public void GrantProviderRegistered()
    {
        var s = new ServiceCollection().AddPubSoftEfCorePermissionGrantProvider<Ctx>();
        Assert.Contains(s, d => d.ServiceType == typeof(IPermissionGrantProvider) && d.ImplementationType == typeof(EfCorePermissionGrantProvider<Ctx>));
    }

    [Fact]
    public void GrantManagerAndScopeProviderRegistered()
    {
        var s = new ServiceCollection()
            .AddPubSoftEfCorePermissionGrantManager<Ctx>()
            .AddPubSoftEfCoreRoleDataScopeProvider<Ctx>();
        Assert.Contains(s, d => d.ServiceType == typeof(EfCorePermissionGrantManager<Ctx>));
        Assert.Contains(s, d => d.ServiceType == typeof(IRoleDataScopeProvider));
    }

    [Fact]
    public void RepositoryAndUowRegistered()
    {
        var s = new ServiceCollection()
            .AddPubSoftRepository<E, long, Ctx>()
            .AddPubSoftUnitOfWork<Ctx>();
        Assert.Contains(s, d => d.ServiceType == typeof(IRepository<E, long>));
        Assert.True(s.Count > 2);
    }

    [Fact]
    public void ServiceDefaultsRegistersHealthCheck()
    {
        var s = new ServiceCollection().AddPubSoftEfCoreServiceDefaults<Ctx>("db");
        Assert.Contains(s, d => d.ServiceType == typeof(PubSoft.Monitoring.IAppHealthCheck));
    }
}
