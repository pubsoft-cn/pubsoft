using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore.Migrations;

public interface IDatabaseSchemaMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

public interface IDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public interface IDatabaseMigrationService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

public sealed class DatabaseMigrationService : IDatabaseMigrationService
{
    private readonly IDatabaseSchemaMigrator[] _schemaMigrators;
    private readonly IDataSeeder[] _dataSeeders;
    private readonly ILogger<DatabaseMigrationService>? _logger;

    public DatabaseMigrationService(
        IEnumerable<IDatabaseSchemaMigrator> schemaMigrators,
        IEnumerable<IDataSeeder> dataSeeders,
        ILogger<DatabaseMigrationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(schemaMigrators);
        ArgumentNullException.ThrowIfNull(dataSeeders);
        _schemaMigrators = schemaMigrators.ToArray();
        _dataSeeders = dataSeeders.ToArray();
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Database migration started.");
        _logger?.LogInformation("Schema migrators: {Count}", _schemaMigrators.Length);
        foreach (var migrator in _schemaMigrators)
            await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Data seeders: {Count}", _dataSeeders.Length);
        foreach (var seeder in _dataSeeders)
            await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Database migration completed.");
    }
}

public sealed class EfCoreDatabaseSchemaMigrator<TDbContext>(
    TDbContext dbContext,
    ILogger<EfCoreDatabaseSchemaMigrator<TDbContext>>? logger = null) : IDatabaseSchemaMigrator
    where TDbContext : DbContext
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var pendingMigrations = (await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)
            ).ToArray();
        if (pendingMigrations.Length == 0)
        {
            logger?.LogDebug("No pending migrations for {DbContext}.", typeof(TDbContext).Name);
            return;
        }

        logger?.LogInformation(
            "Applying {Count} pending migrations for {DbContext}.",
            pendingMigrations.Length,
            typeof(TDbContext).Name);
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}

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