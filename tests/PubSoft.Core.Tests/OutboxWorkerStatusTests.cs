using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;
using PubSoft.Testing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboxWorkerStatusTests
{
    [Fact]
    public void TrackerCountsConsecutivePollFailuresAndResetsOnSuccess()
    {
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var tracker = new OutboxWorkerStatusTracker(time);
        tracker.RecordPollStarted();
        tracker.RecordPollFailure("InvalidOperationException");
        tracker.RecordPollStarted();
        tracker.RecordPollFailure("TimeoutException");
        var snapshot = tracker.Snapshot();
        Assert.Equal(2, snapshot.ConsecutivePollFailures);
        Assert.Equal("TimeoutException", snapshot.LastErrorType);
        Assert.Equal(2, snapshot.TotalPolls);
        Assert.Equal(time.GetUtcNow(), snapshot.LastPollCompletedUtc);
        tracker.RecordPollStarted();
        tracker.RecordPollSucceeded();
        snapshot = tracker.Snapshot();
        Assert.Equal(0, snapshot.ConsecutivePollFailures);
        Assert.Equal("TimeoutException", snapshot.LastErrorType);
    }

    [Fact]
    public void TrackerRejectsNegativeBatchCountsAndBlankErrorTypes()
    {
        var tracker = new OutboxWorkerStatusTracker();
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.RecordBatchCompleted(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.RecordBatchCompleted(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.RecordBatchCompleted(0, 0, -1));
        Assert.Throws<ArgumentException>(() => tracker.RecordPollFailure(""));
    }

    [Fact]
    public void TrackerSnapshotIsIndependentOfLaterMutations()
    {
        var tracker = new OutboxWorkerStatusTracker();
        tracker.RecordBatchCompleted(2, 1, 1);
        var first = tracker.Snapshot();
        tracker.RecordBatchCompleted(3, 3, 0);
        Assert.Equal(1, first.TotalDelivered);
        Assert.Equal(1, first.TotalDispatchFailures);
        var second = tracker.Snapshot();
        Assert.Equal(4, second.TotalDelivered);
        Assert.Equal(3, second.LastBatchClaimedCount);
    }

    [Fact]
    public async Task WorkerReportsDeliveredBatchesIntoSingletonTracker()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"status-outbox-{TestIds.Next():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton(dispatched);
        services.AddSingleton<ITenantContext>(new NullTenantContext());
        services.AddScoped<IOutboxDispatcher, SignalingOutboxDispatcher>();
        services.AddPubSoftOutbox<OutboxDbContext>(options => options.PollInterval = TimeSpan.FromHours(1));
        services.AddPubSoftOutboxWorker();
        await using var provider = services.BuildServiceProvider();
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            await context.Database.EnsureCreatedAsync();
        }

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            await new EfCoreOutboxPublisher<OutboxDbContext>(context, TestIds.Shared)
                .PublishAsync(new OrderPlaced(1), "orders.order-placed.v1");
            await context.SaveChangesAsync();
        }

        var worker = Assert.Single(provider.GetServices<IHostedService>().OfType<OutboxBackgroundService>());
        await worker.StartAsync(default);
        try
        {
            await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            OutboxWorkerStatus snapshot;
            var tracker = provider.GetRequiredService<OutboxWorkerStatusTracker>();
            do
            {
                snapshot = tracker.Snapshot();
                if (snapshot.TotalDelivered >= 1)
                    break;
                await Task.Delay(10);
            }
            while (DateTime.UtcNow < deadline);
            Assert.True(snapshot.TotalDelivered >= 1, $"expected delivery, got {snapshot.TotalDelivered}");
            Assert.Equal(0, snapshot.ConsecutivePollFailures);
            Assert.Equal(1, snapshot.LastBatchClaimedCount);
            Assert.Equal(1, snapshot.LastBatchDeliveredCount);
            Assert.NotNull(snapshot.LastPollStartedUtc);
            Assert.NotNull(snapshot.LastPollCompletedUtc);
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    [Fact]
    public async Task ProcessorRecordsDispatchFailures()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"status-fail-outbox-{TestIds.Next():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<ITenantContext>(new NullTenantContext());
        services.AddScoped<IOutboxDispatcher, ThrowingOutboxDispatcher>();
        services.AddPubSoftOutbox<OutboxDbContext>(options => options.PollInterval = TimeSpan.FromHours(1));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        await context.Database.EnsureCreatedAsync();
        await new EfCoreOutboxPublisher<OutboxDbContext>(context, TestIds.Shared)
            .PublishAsync(new OrderPlaced(1), "orders.order-placed.v1");
        await context.SaveChangesAsync();
        var processor = new OutboxProcessor(
            scope.ServiceProvider.GetRequiredService<IOutboxStore>(),
            scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>(),
            statusTracker: scope.ServiceProvider.GetRequiredService<OutboxWorkerStatusTracker>());
        var delivered = await processor.ProcessBatchAsync();
        Assert.Equal(0, delivered);
        var snapshot = scope.ServiceProvider.GetRequiredService<OutboxWorkerStatusTracker>().Snapshot();
        Assert.Equal(0, snapshot.TotalDelivered);
        Assert.Equal(1, snapshot.TotalDispatchFailures);
        Assert.Equal(1, snapshot.LastBatchClaimedCount);
    }

    [Fact]
    public async Task WorkerRecordsIterationLevelPollFailures()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new NullTenantContext());
        services.AddScoped<IOutboxStore, ThrowingOutboxStore>();
        services.AddScoped<IOutboxDispatcher, ThrowingOutboxDispatcher>();
        services.AddPubSoftOutbox<OutboxDbContext>(options => options.PollInterval = TimeSpan.FromMilliseconds(20));
        services.AddPubSoftOutboxWorker();
        await using var provider = services.BuildServiceProvider();
        var worker = Assert.Single(provider.GetServices<IHostedService>().OfType<OutboxBackgroundService>());
        await worker.StartAsync(default);
        try
        {
            var tracker = provider.GetRequiredService<OutboxWorkerStatusTracker>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            OutboxWorkerStatus snapshot;
            do
            {
                snapshot = tracker.Snapshot();
                if (snapshot.ConsecutivePollFailures >= 2)
                    break;
                await Task.Delay(15);
            }
            while (DateTime.UtcNow < deadline);
            Assert.True(snapshot.ConsecutivePollFailures >= 2, $"expected consecutive failures, got {snapshot.ConsecutivePollFailures}");
            Assert.Equal("InvalidOperationException", snapshot.LastErrorType);
            Assert.True(snapshot.TotalPolls >= 2);
            Assert.Equal(0, snapshot.TotalDelivered);
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    private sealed class ThrowingOutboxStore : IOutboxStore
    {
        public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated database outage");
        public Task<bool> MarkDeliveredAsync(long messageId, Guid leaseToken, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<bool> MarkFailedAsync(long messageId, Guid leaseToken, string errorCode, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed record OrderPlaced(int OrderId);

    private sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options, ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftOutbox();
        }
    }

    private sealed class NullTenantContext : ITenantContext
    {
        public long? TenantId => null;
    }

    private sealed class SignalingOutboxDispatcher(TaskCompletionSource dispatched) : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            dispatched.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOutboxDispatcher : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated broker outage");
    }
}
