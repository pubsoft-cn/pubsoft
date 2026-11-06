using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore.Migrations;


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
