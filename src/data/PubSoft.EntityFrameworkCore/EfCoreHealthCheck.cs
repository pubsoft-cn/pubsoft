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
        EfCoreHealthCheckGuards.RequireConcreteContextType<TDbContext>();
        _serviceProvider = serviceProvider;
        _probeSql = probeSql;
        Name = name;
    }

    public string Name { get; }

    public async ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<TDbContext>>();
        await using var db = factory is not null
            ? await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false)
            : scope.ServiceProvider.GetRequiredService<TDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            return AppHealthCheckResult.Unhealthy($"{Name}: cannot connect");
        await db.Database.ExecuteSqlRawAsync(_probeSql, cancellationToken).ConfigureAwait(false);
        return AppHealthCheckResult.Healthy();
    }
}

internal static class EfCoreHealthCheckGuards
{
    // AddDbContext<T>/AddDbContextFactory<T> only ever register concrete context types; a base
    // or abstract TDbContext can never resolve and would leave health checks permanently unhealthy.
    public static void RequireConcreteContextType<TDbContext>() where TDbContext : DbContext
    {
        if (typeof(TDbContext) == typeof(DbContext) || typeof(TDbContext).IsAbstract)
            throw new ArgumentException(
                $"Pass the concrete DbContext type registered via AddDbContext<T>/AddDbContextFactory<T>; '{typeof(TDbContext).Name}' is not resolvable.");
    }
}
