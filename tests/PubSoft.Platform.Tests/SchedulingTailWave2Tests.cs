using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Scheduling;
using PubSoft.Scheduling.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulingTailWave2Tests
{
    private sealed class Exec(string type) : IJobExecutor
    {
        public string Type => type;
        public Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken ct) => Task.FromResult(new JobResult());
    }

    private sealed class Proc(string name) : IResultProcessor
    {
        public string Name => name;
        public Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class State { public int N { get; set; } }

    [Fact]
    public async Task MemoryStateStoreCustomStateRoundTrip()
    {
        var s = new MemoryStateStore();
        Assert.Null(await s.GetCustomStateAsync<State>("k", CancellationToken.None));
        await s.SetCustomStateAsync("k", new State { N = 5 }, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(5, (await s.GetCustomStateAsync<State>("k", CancellationToken.None))!.N);
        await s.RemoveCustomStateAsync("k", CancellationToken.None);
        Assert.Null(await s.GetCustomStateAsync<State>("k", CancellationToken.None));
    }

    [Fact]
    public async Task CustomStateRejectsBadArgs()
    {
        var s = new MemoryStateStore();
        await Assert.ThrowsAsync<ArgumentException>(() => s.SetCustomStateAsync(" ", new State(), TimeSpan.FromMinutes(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => s.SetCustomStateAsync("k", (State?)null!, TimeSpan.FromMinutes(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => s.SetCustomStateAsync("k", new State(), TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public void AddPubSoftSchedulerRegistersCore()
    {
        var s = new ServiceCollection();
        s.AddPubSoftScheduler().AddExecutor<Exec>().AddResultProcessor<Proc>();
        Assert.Contains(s, d => d.ServiceType == typeof(IExecutorRegistry));
        Assert.Contains(s, d => d.ServiceType == typeof(IResultProcessorRegistry));
        Assert.Contains(s, d => d.ServiceType == typeof(IStateStore));
        Assert.Contains(s, d => d.ServiceType == typeof(SchedulerEngine));
    }

    [Fact]
    public void SchedulerHostedServiceIsHosted()
    {
        var s = new ServiceCollection();
        s.AddPubSoftScheduler();
        Assert.Contains(s, d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
    }

    [Fact]
    public async Task EfCoreStateStoreCustomStateRoundTripOverwriteAndRemove()
    {
        await using var harness = await EfHarness.CreateAsync();
        Assert.Null(await harness.Store.GetCustomStateAsync<State>("k", CancellationToken.None));

        await harness.Store.SetCustomStateAsync("k", new State { N = 5 }, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(5, (await harness.Store.GetCustomStateAsync<State>("k", CancellationToken.None))!.N);

        await harness.Store.SetCustomStateAsync("k", new State { N = 9 }, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(9, (await harness.Store.GetCustomStateAsync<State>("k", CancellationToken.None))!.N);

        await harness.Store.RemoveCustomStateAsync("k", CancellationToken.None);
        Assert.Null(await harness.Store.GetCustomStateAsync<State>("k", CancellationToken.None));
    }

    [Fact]
    public async Task EfCoreStateStoreExpiredEntryReadsNull()
    {
        await using var harness = await EfHarness.CreateAsync();
        await harness.Store.SetCustomStateAsync("k", new State { N = 5 }, TimeSpan.FromMinutes(1), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await harness.Store.GetCustomStateAsync<State>("k", CancellationToken.None));
    }

    [Fact]
    public async Task EfCoreStateStoreConcurrentUpsertConverges()
    {
        await using var harness = await EfHarness.CreateAsync();
        await Task.WhenAll(Enumerable.Range(1, 8).Select(index =>
            harness.Store.SetCustomStateAsync("k", new State { N = index }, TimeSpan.FromMinutes(1), CancellationToken.None)));

        var value = await harness.Store.GetCustomStateAsync<State>("k", CancellationToken.None);
        Assert.NotNull(value);
        Assert.InRange(value!.N, 1, 8);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }

    private sealed record StubTenantContext(long? TenantId) : PubSoft.Domain.ITenantContext;

    private sealed class FakeDbContext(DbContextOptions options)
        : PubSoft.EntityFrameworkCore.PubSoftDbContext(options, new StubTenantContext(null), null, null, null)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftSchedulerState();
        }
    }

    private sealed class EfHarness : IAsyncDisposable
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private EfHarness(Microsoft.Data.Sqlite.SqliteConnection connection, ServiceProvider provider, PubSoft.Scheduling.EntityFrameworkCore.EfCoreStateStore<FakeDbContext> store, MutableClock clock)
        {
            _connection = connection;
            _provider = provider;
            Store = store;
            Clock = clock;
        }

        public PubSoft.Scheduling.EntityFrameworkCore.EfCoreStateStore<FakeDbContext> Store { get; }
        public MutableClock Clock { get; }

        public static async Task<EfHarness> CreateAsync()
        {
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var clock = new MutableClock(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
            var services = new ServiceCollection();
            services.AddSingleton<PubSoft.Domain.ITenantContext>(new StubTenantContext(null));
            services.AddDbContext<FakeDbContext>(options => options.UseSqlite(connection));
            var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
                scope.ServiceProvider.GetRequiredService<FakeDbContext>().Database.EnsureCreated();
            var store = new PubSoft.Scheduling.EntityFrameworkCore.EfCoreStateStore<FakeDbContext>(
                provider.GetRequiredService<IServiceScopeFactory>(), clock);
            return new EfHarness(connection, provider, store, clock);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
