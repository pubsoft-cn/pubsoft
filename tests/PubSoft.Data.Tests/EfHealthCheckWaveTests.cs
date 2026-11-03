using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.EntityFrameworkCore;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EfHealthCheckWaveTests
{
    private sealed class Ctx(DbContextOptions<Ctx> o) : DbContext(o) { }

    [Fact]
    public async Task HealthyWhenContextConnects()
    {
        var services = new ServiceCollection();
        services.AddDbContext<Ctx>(o => o.UseSqlite("Data Source=:memory:"));
        var sp = services.BuildServiceProvider();
        var check = new EfCoreHealthCheck<Ctx>(sp);
        var r = await check.CheckAsync();
        Assert.Equal(AppHealthStatus.Healthy, r.Status);
        Assert.Equal("db", check.Name);
    }

    [Fact]
    public async Task UnresolvableContextPropagatesException()
    {
        // healthcheck does not swallow DI resolution failures — misconfiguration surfaces
        var sp = new ServiceCollection().BuildServiceProvider();
        var check = new EfCoreHealthCheck<Ctx>(sp);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await check.CheckAsync());
    }
}
