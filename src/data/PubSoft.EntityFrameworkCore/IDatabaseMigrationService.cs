namespace PubSoft.EntityFrameworkCore.Migrations;


/// <summary>Hosted migration entry point.</summary>
public interface IDatabaseMigrationService
{
    /// <summary>Runs pending migrations.</summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
