using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EfCoreHealthCheckWaveTests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant()) { }

    private static (Ctx, ServiceProvider) Build()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<Ctx>().UseSqlite(conn).Options;
        var c = new Ctx(opts); c.Database.EnsureCreated();
        var sc = new ServiceCollection();
        sc.AddSingleton(c);
        return (c, sc.BuildServiceProvider());
    }

    [Fact]
    public async Task HealthyProbe()
    {
        var (_, sp) = Build();
        var check = new EfCoreHealthCheck<Ctx>(sp, "db");
        var r = await check.CheckAsync();
        Assert.Equal(AppHealthStatus.Healthy, r.Status);
        Assert.Equal("db", check.Name);
    }

    [Fact]
    public async Task BadProbeSqlThrows()
    {
        var (_, sp) = Build();
        var check = new EfCoreHealthCheck<Ctx>(sp, "db", "SELECT * FROM no_such_table_xyz");
        await Assert.ThrowsAnyAsync<Exception>(async () => await check.CheckAsync()); // probe exception propagates
    }

    [Fact]
    public async Task MissingCtxThrows()
    {
        var sc = new ServiceCollection(); // no Ctx registered — resolution propagates
        var check = new EfCoreHealthCheck<Ctx>(sc.BuildServiceProvider(), "db");
        await Assert.ThrowsAnyAsync<Exception>(async () => await check.CheckAsync());
    }
}
