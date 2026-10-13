using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Authorization;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Migrations;

var executionOrder = new List<string>();
var services = new ServiceCollection();
services.AddPubSoftDatabaseMigrationService();
services.AddPubSoftEfCorePermissionGrantProvider<SmokeDbContext>();
services.AddPubSoftEfCorePermissionGrantManager<SmokeDbContext>();
if (!services.Any(descriptor => descriptor.ServiceType == typeof(IPermissionGrantProvider))
    || !services.Any(descriptor => descriptor.ServiceType == typeof(EfCorePermissionGrantManager<SmokeDbContext>)))
    throw new InvalidOperationException("EF Core permission services were not registered.");
services.AddSingleton<IDatabaseSchemaMigrator>(new SmokeSchemaMigrator(executionOrder));
services.AddSingleton<IDataSeeder>(new SmokeDataSeeder(executionOrder));

using var provider = services.BuildServiceProvider();
await provider.GetRequiredService<IDatabaseMigrationService>().RunAsync();
if (!executionOrder.SequenceEqual(["schema", "seed"]))
    throw new InvalidOperationException("EF Core migration consumer check failed.");

Console.WriteLine("Isolated PubSoft.EntityFrameworkCore consumer smoke check passed.");

sealed class SmokeSchemaMigrator(List<string> executionOrder) : IDatabaseSchemaMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        executionOrder.Add("schema");
        return Task.CompletedTask;
    }
}

sealed class SmokeDataSeeder(List<string> executionOrder) : IDataSeeder
{
    public Task SeedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        executionOrder.Add("seed");
        return Task.CompletedTask;
    }
}

sealed class SmokeDbContext(DbContextOptions<SmokeDbContext> options, ITenantContext tenantContext)
    : PubSoftDbContext(options, tenantContext)
{
}