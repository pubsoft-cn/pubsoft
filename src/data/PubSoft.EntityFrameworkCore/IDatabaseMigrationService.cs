using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore.Migrations;


/// <summary>Hosted migration entry point.</summary>
public interface IDatabaseMigrationService
{
    /// <summary>Runs pending migrations.</summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
