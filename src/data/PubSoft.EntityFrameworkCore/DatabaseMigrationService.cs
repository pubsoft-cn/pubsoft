using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore.Migrations;


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
