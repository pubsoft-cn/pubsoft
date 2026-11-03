using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;

namespace PubSoft.EntityFrameworkCore;

public static class EfCoreHealthCheckExtensions
{
    public static IServiceCollection AddPubSoftEfCoreHealthCheck<TDbContext>(
        this IServiceCollection services, string name = "db", string probeSql = "SELECT 1")
        where TDbContext : DbContext
    {
        services.AddSingleton<IAppHealthCheck>(sp => new EfCoreHealthCheck<TDbContext>(sp, name, probeSql));
        return services;
    }
}

public static class EfCoreServiceDefaultsExtensions
{
    public static IServiceCollection AddPubSoftEfCoreServiceDefaults<TDbContext>(
        this IServiceCollection services, string healthCheckName = "db")
        where TDbContext : DbContext
    {
        services.AddPubSoftEfCoreHealthCheck<TDbContext>(healthCheckName);
        services.AddPubSoftDbQueryDiagnostics();
        return services;
    }
}
