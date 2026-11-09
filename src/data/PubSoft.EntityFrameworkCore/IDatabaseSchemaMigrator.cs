using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore.Migrations;

/// <summary>Schema-migration seam for a DbContext.</summary>
public interface IDatabaseSchemaMigrator
{
    /// <summary>Applies pending migrations.</summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
