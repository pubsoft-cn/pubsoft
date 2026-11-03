using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;

namespace PubSoft.EntityFrameworkCore;

public sealed class EfCoreHealthCheck<TDbContext> : IAppHealthCheck where TDbContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _probeSql;

    public EfCoreHealthCheck(IServiceProvider serviceProvider, string name = "db", string probeSql = "SELECT 1")
    {
        _serviceProvider = serviceProvider;
        _probeSql = probeSql;
        Name = name;
    }

    public string Name { get; }

    public async ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            return AppHealthCheckResult.Unhealthy($"{Name}: cannot connect");
        await db.Database.ExecuteSqlRawAsync(_probeSql, cancellationToken).ConfigureAwait(false);
        return AppHealthCheckResult.Healthy();
    }
}
