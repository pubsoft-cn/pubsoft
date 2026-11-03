using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Scheduling;
using PubSoft.Scheduling.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulingEfCoreStateStoreTests
{
    [Fact]
    public async Task StorePersistsValuesAcrossScopesAndSeparatesTenants()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-02T00:00:00Z"));
        var tenantContext = new TestTenantContext(1);
        var services = new ServiceCollection();
        services.AddDbContext<TestStateDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<TimeProvider>(clock);
        services.AddPubSoftEfCoreSchedulerStateStore<TestStateDbContext>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IStateStore>();

        await using (var setupScope = provider.CreateAsyncScope())
            await setupScope.ServiceProvider.GetRequiredService<TestStateDbContext>().Database.EnsureCreatedAsync();

        var firstResult = new JobResult
        {
            Success = false,
            StatusCode = 503,
            ErrorMessage = "failed",
            ExecutedAt = clock.GetUtcNow()
        };
        await store.UpdateStateAsync("nightly", firstResult, default);
        await store.SetCustomStateAsync("counter", new StateValue(7), TimeSpan.FromSeconds(5), default);

        tenantContext.TenantId = 2;
        Assert.Null(await store.GetLastResultAsync("nightly", default));
        Assert.Null(await store.GetCustomStateAsync<StateValue>("counter", default));
        await store.SetCustomStateAsync("counter", new StateValue(99), null, default);

        tenantContext.TenantId = 1;
        var restored = await store.GetLastResultAsync("nightly", default);
        Assert.NotNull(restored);
        Assert.Equal(503, restored.StatusCode);
        Assert.Equal("failed", restored.ErrorMessage);
        Assert.Equal(7, (await store.GetCustomStateAsync<StateValue>("counter", default))?.Value);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(await store.GetCustomStateAsync<StateValue>("counter", default));
        await store.RemoveCustomStateAsync("counter", default);

        clock.Advance(TimeSpan.FromDays(8));
        Assert.Null(await store.GetLastResultAsync("nightly", default));
    }

    [Fact]
    public async Task StateKeyIsUniqueWithinTenantScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<TestStateDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ITenantContext>(new TestTenantContext(1));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestStateDbContext>();
        await context.Database.EnsureCreatedAsync();

        var keyHash = new string('A', 64);
        context.Set<SchedulerStateRecord>().Add(new SchedulerStateRecord
        {
            TenantId = 1,
            TenantScope = "tenant:1",
            Key = "custom:counter",
            KeyHash = keyHash,
            ValueJson = "{}",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var duplicate = new SchedulerStateRecord
        {
            TenantId = 1,
            TenantScope = "tenant:1",
            Key = "custom:counter",
            KeyHash = keyHash,
            ValueJson = "{\"value\":2}",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.Set<SchedulerStateRecord>().Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.Entry(duplicate).State = EntityState.Detached;

        context.Set<SchedulerStateRecord>().Add(new SchedulerStateRecord
        {
            TenantId = 2,
            TenantScope = "tenant:2",
            Key = "custom:counter",
            KeyHash = keyHash,
            ValueJson = "{\"value\":3}",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Set<SchedulerStateRecord>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task StoreUpdatesTheWinningRowWhenConcurrentInsertWins()
    {
        const string connectionString = "Data Source=SchedulerStateRace;Mode=Memory;Cache=Shared";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var competingInsert = new CompetingSchedulerStateInsertInterceptor(connectionString);
        var services = new ServiceCollection();
        services.AddDbContext<TestStateDbContext>(options =>
            options.UseSqlite(connectionString).AddInterceptors(competingInsert));
        services.AddSingleton<ITenantContext>(new TestTenantContext(1));
        services.AddPubSoftEfCoreSchedulerStateStore<TestStateDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<TestStateDbContext>();
            await context.Database.EnsureCreatedAsync();
        }

        var store = provider.GetRequiredService<IStateStore>();
        await store.SetCustomStateAsync("race", new StateValue(2), null, default);

        Assert.Equal(2, (await store.GetCustomStateAsync<StateValue>("race", default))?.Value);
    }

    [Fact]
    public void RegistrationReplacesTheDefaultMemoryStateStore()
    {
        var services = new ServiceCollection();
        services.AddPubSoftScheduler();
        services.AddDbContext<TestStateDbContext>(_ => { });
        services.AddSingleton<ITenantContext>(new TestTenantContext(1));
        services.AddPubSoftEfCoreSchedulerStateStore<TestStateDbContext>();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreStateStore<TestStateDbContext>>(provider.GetRequiredService<IStateStore>());
    }

    private sealed class TestStateDbContext(
        DbContextOptions<TestStateDbContext> options,
        ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftSchedulerState();
        }
    }

    private sealed class TestTenantContext(long? tenantId) : ITenantContext
    {
        public long? TenantId { get; set; } = tenantId;
    }

    private sealed record StateValue(int Value);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class CompetingSchedulerStateInsertInterceptor(string connectionString)
        : SaveChangesInterceptor
    {
        private int _inserted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _inserted, 1) == 0)
            {
                var options = new DbContextOptionsBuilder<TestStateDbContext>()
                    .UseSqlite(connectionString)
                    .Options;
                await using var context = new TestStateDbContext(options, new TestTenantContext(1));
                context.Set<SchedulerStateRecord>().Add(new SchedulerStateRecord
                {
                    TenantId = 1,
                    TenantScope = "tenant:1",
                    Key = "custom:race",
                    KeyHash = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes("custom:race"))),
                    ValueJson = "{\"value\":1}",
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await context.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}