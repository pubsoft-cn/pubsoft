using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Scheduling;
using PubSoft.Scheduling.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var services = new ServiceCollection();
services.AddPubSoftScheduler();
services.AddDbContext<SmokeDbContext>(options => options.UseSqlite(connection));
services.AddSingleton<ITenantContext, SmokeTenantContext>();
services.AddPubSoftEfCoreSchedulerStateStore<SmokeDbContext>();

await using var provider = services.BuildServiceProvider();
await using (var setupScope = provider.CreateAsyncScope())
    await setupScope.ServiceProvider.GetRequiredService<SmokeDbContext>().Database.EnsureCreatedAsync();

var store = provider.GetRequiredService<IStateStore>();
var expected = new JobResult { Success = true, StatusCode = 204 };
await store.UpdateStateAsync("consumer-smoke", expected, default);
await store.SetCustomStateAsync("checkpoint", new SmokeState("persisted"), null, default);

var lastResult = await store.GetLastResultAsync("consumer-smoke", default);
var customState = await store.GetCustomStateAsync<SmokeState>("checkpoint", default);
if (lastResult is not { Success: true, StatusCode: 204 }
    || customState?.Value != "persisted"
    || store is not EfCoreStateStore<SmokeDbContext>)
    throw new InvalidOperationException("Scheduler EF persistence consumer check failed.");

Console.WriteLine("Isolated PubSoft.Scheduling.EntityFrameworkCore consumer persistence check passed.");

sealed class SmokeDbContext(DbContextOptions<SmokeDbContext> options, ITenantContext tenantContext)
    : PubSoftDbContext(options, tenantContext)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigurePubSoftSchedulerState();
    }
}

sealed class SmokeTenantContext : ITenantContext
{
    public long? TenantId => 7;
}

sealed record SmokeState(string Value);