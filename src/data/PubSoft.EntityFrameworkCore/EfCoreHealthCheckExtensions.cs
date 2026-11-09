using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Registration for the EF health check.</summary>
public static class EfCoreHealthCheckExtensions
{
    /// <summary>Registers the DbContext probe check.</summary>
    public static IServiceCollection AddPubSoftEfCoreHealthCheck<TDbContext>(
        this IServiceCollection services, string name = "db", string probeSql = "SELECT 1")
        where TDbContext : DbContext
    {
        // Fail at registration, not at first /health probe (silent-503 in alpha.10).
        EfCoreHealthCheckGuards.RequireConcreteContextType<TDbContext>();
        services.AddSingleton<IAppHealthCheck>(sp => new EfCoreHealthCheck<TDbContext>(sp, name, probeSql));
        return services;
    }
}

/// <summary>Opinionated EF service defaults.</summary>
public static class EfCoreServiceDefaultsExtensions
{
    /// <summary>Registers the EF default service set.</summary>
    public static IServiceCollection AddPubSoftEfCoreServiceDefaults<TDbContext>(
        this IServiceCollection services, string healthCheckName = "db")
        where TDbContext : DbContext
    {
        services.AddPubSoftHealthChecks();
        services.AddPubSoftEfCoreHealthCheck<TDbContext>(healthCheckName);
        services.AddPubSoftDbQueryDiagnostics();
        return services;
    }
}
