using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.EntityFrameworkCore;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

/// <summary>EfCoreHealthCheck: live probe paths.</summary>
public sealed class EfHealthCheckWaveTests
{
    private sealed class Ctx(DbContextOptions<Ctx> o) : DbContext(o);

    private static ServiceProvider Build(string connStr)
        => new ServiceCollection()
            .AddDbContext<Ctx>(b => b.UseSqlite(connStr))
            .BuildServiceProvider();

    [Fact]
    public async Task HealthyWhenProbeSucceeds()
    {
        await using var anchor = new SqliteConnection("Data Source=h1;Mode=Memory;Cache=Shared");
        await anchor.OpenAsync();
        var sp = Build("Data Source=h1;Mode=Memory;Cache=Shared");
        var check = new EfCoreHealthCheck<Ctx>(sp);
        var result = await check.CheckAsync();
        Assert.Equal(AppHealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task UnhealthyWhenCannotConnect()
    {
        // SQLite open_mode=12 opens read-only nonexistent - CanConnect fails cleanly
        var sp = Build("Data Source=/nonexistent-dir/no.db");
        var check = new EfCoreHealthCheck<Ctx>(sp);
        var result = await check.CheckAsync();
        Assert.NotEqual(AppHealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task BadProbeSqlThrowsNotUnhealthy()
    {
        await using var anchor = new SqliteConnection("Data Source=h2;Mode=Memory;Cache=Shared");
        await anchor.OpenAsync();
        var sp = Build("Data Source=h2;Mode=Memory;Cache=Shared");
        var check = new EfCoreHealthCheck<Ctx>(sp, probeSql: "SELECT nonsense_col");
        // probe failure propagates - health check surfaces as exception not silent Healthy
        await Assert.ThrowsAnyAsync<Exception>(() => check.CheckAsync().AsTask());
    }
}
