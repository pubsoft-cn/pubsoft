namespace PubSoft.EntityFrameworkCore.Migrations;

/// <summary>Schema-migration seam for a DbContext.</summary>
public interface IDatabaseSchemaMigrator
{
    /// <summary>Applies pending migrations.</summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
