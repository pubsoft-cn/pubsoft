using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.EntityFrameworkCore.Migrations;


/// <summary>Registration for database migration.</summary>
public static class DatabaseMigrationServiceCollectionExtensions
{
    /// <summary>Registers the migration hosted service.</summary>
    public static IServiceCollection AddPubSoftDatabaseMigrationService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IDatabaseMigrationService, DatabaseMigrationService>();
        return services;
    }

    /// <summary>Registers EF-based schema migration for <typeparamref name="TDbContext"/>.</summary>
    public static IServiceCollection AddPubSoftEfCoreMigration<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<IDatabaseSchemaMigrator, EfCoreDatabaseSchemaMigrator<TDbContext>>();
        return services;
    }
}
