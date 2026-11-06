using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore.Migrations;


public static class DatabaseMigrationServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftDatabaseMigrationService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IDatabaseMigrationService, DatabaseMigrationService>();
        return services;
    }

    public static IServiceCollection AddPubSoftEfCoreMigration<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<IDatabaseSchemaMigrator, EfCoreDatabaseSchemaMigrator<TDbContext>>();
        return services;
    }
}
