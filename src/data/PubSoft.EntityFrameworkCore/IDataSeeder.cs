namespace PubSoft.EntityFrameworkCore.Migrations;


/// <summary>Host-owned seed hook run at startup.</summary>
public interface IDataSeeder
{
    /// <summary>Runs the seed.</summary>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
