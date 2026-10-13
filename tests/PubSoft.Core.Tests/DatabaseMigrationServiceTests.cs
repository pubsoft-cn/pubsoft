using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DatabaseMigrationServiceTests
{
    [Fact]
    public async Task RunAsync_MigratesSchemasInOrderBeforeSeeding()
    {
        var events = new List<string>();
        var service = new DatabaseMigrationService(
            [new RecordingMigrator("schema-1", events), new RecordingMigrator("schema-2", events)],
            [new RecordingSeeder("seed-1", events), new RecordingSeeder("seed-2", events)]);

        await service.RunAsync();

        Assert.Equal(["schema-1", "schema-2", "seed-1", "seed-2"], events);
    }

    [Fact]
    public async Task RunAsync_StopsBeforeSeedingWhenSchemaMigrationFails()
    {
        var events = new List<string>();
        var service = new DatabaseMigrationService(
            [new RecordingMigrator("schema-1", events), new FailingMigrator(events)],
            [new RecordingSeeder("seed", events)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync());

        Assert.Equal(["schema-1", "schema-failed"], events);
    }

    [Fact]
    public async Task EfCoreRegistrationRunsOnlyWhenMigrationServiceIsInvoked()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddDbContext<EmptyMigrationDbContext>(options => options.UseSqlite(connection));
        services.AddPubSoftDatabaseMigrationService();
        services.AddPubSoftEfCoreMigration<EmptyMigrationDbContext>();
        services.AddTransient<IDataSeeder>(_ => new RecordingSeeder("seed", events));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrationService = scope.ServiceProvider.GetRequiredService<IDatabaseMigrationService>();

        Assert.Empty(events);
        await migrationService.RunAsync();
        Assert.Equal(["seed"], events);
    }

    private sealed class EmptyMigrationDbContext(DbContextOptions<EmptyMigrationDbContext> options) : DbContext(options);

    private sealed class RecordingMigrator(string name, List<string> events) : IDatabaseSchemaMigrator
    {
        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingMigrator(List<string> events) : IDatabaseSchemaMigrator
    {
        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            events.Add("schema-failed");
            throw new InvalidOperationException("Schema migration failed.");
        }
    }

    private sealed class RecordingSeeder(string name, List<string> events) : IDataSeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(name);
            return Task.CompletedTask;
        }
    }
}