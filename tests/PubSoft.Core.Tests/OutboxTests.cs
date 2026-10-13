using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboxTests
{
    [Fact]
    public async Task BackgroundWorkerProcessesEachHostProvidedTenantScope()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"tenant-outbox-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var dispatchedTenants = new ConcurrentQueue<long?>();
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<MutableTenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<MutableTenantContext>());
        services.AddSingleton<IOutboxScopeProvider, TestOutboxScopeProvider>();
        services.AddSingleton(dispatchedTenants);
        services.AddSingleton(dispatched);
        services.AddScoped<IOutboxDispatcher, RecordingOutboxDispatcher>();
        services.AddPubSoftOutbox<OutboxDbContext>(options => options.PollInterval = TimeSpan.FromHours(1));
        services.AddPubSoftOutboxWorker();
        await using var provider = services.BuildServiceProvider();

        await using (var setupScope = provider.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            await context.Database.EnsureCreatedAsync();
        }

        foreach (var tenantId in new long[] { 101, 202 })
        {
            await using var seedScope = provider.CreateAsyncScope();
            seedScope.ServiceProvider.GetRequiredService<MutableTenantContext>().TenantId = tenantId;
            var context = seedScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            await new EfCoreOutboxPublisher<OutboxDbContext>(context)
                .PublishAsync(new OrderPlaced((int)tenantId));
            await context.SaveChangesAsync();
        }

        var worker = Assert.Single(provider.GetServices<IHostedService>().OfType<OutboxBackgroundService>());
        var deliveryCompleted = false;
        await worker.StartAsync(default);
        try
        {
            await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var deliveryDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deliveryDeadline)
            {
                deliveryCompleted = true;
                foreach (var tenantId in new long[] { 101, 202 })
                {
                    await using var verifyScope = provider.CreateAsyncScope();
                    verifyScope.ServiceProvider.GetRequiredService<MutableTenantContext>().TenantId = tenantId;
                    var context = verifyScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
                    deliveryCompleted &= await context.OutboxMessages.AnyAsync(
                        message => message.Status == OutboxMessageStatus.Delivered);
                }

                if (deliveryCompleted)
                    break;

                await Task.Delay(10);
            }

            Assert.True(deliveryCompleted, "The worker did not mark both tenant messages as delivered.");
        }
        finally
        {
            await worker.StopAsync(default);
        }

        Assert.Equal(new long?[] { 101, 202 }, dispatchedTenants.Order().ToArray());
        foreach (var tenantId in new long[] { 101, 202 })
        {
            await using var verifyScope = provider.CreateAsyncScope();
            verifyScope.ServiceProvider.GetRequiredService<MutableTenantContext>().TenantId = tenantId;
            var context = verifyScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            Assert.Equal(OutboxMessageStatus.Delivered, (await context.OutboxMessages.SingleAsync()).Status);
        }
    }

    [Fact]
    public async Task PublisherStagesMessageAndBusinessEntityUntilTheSameSaveChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        var publisher = new EfCoreOutboxPublisher<OutboxDbContext>(context);
        var order = new OrderRecord { Id = 5, Name = "created" };
        context.Orders.Add(order);

        var messageId = await publisher.PublishAsync(new OrderPlaced(5), "billing", "order:5");

        Assert.Empty(await context.Orders.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await context.OutboxMessages.IgnoreQueryFilters().ToListAsync());
        await context.SaveChangesAsync();

        var persistedOrder = await context.Orders.SingleAsync();
        var persistedMessage = await context.OutboxMessages.SingleAsync();
        Assert.Equal(73, persistedOrder.TenantId);
        Assert.Equal(73, persistedMessage.TenantId);
        Assert.Equal(messageId, persistedMessage.Id);
        Assert.Equal("billing", persistedMessage.TargetModule);
        Assert.Equal("order:5", persistedMessage.AggregateKey);
        Assert.Equal(OutboxMessageStatus.Pending, persistedMessage.Status);
        Assert.Contains("\"OrderId\":5", persistedMessage.PayloadJson);
    }

    [Fact]
    public async Task StoreClaimsOnlyMessagesVisibleInTheCurrentTenantScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        Guid otherTenantMessageId;
        await using (var otherTenant = new OutboxDbContext(options, new TenantContext(41)))
        {
            await otherTenant.Database.EnsureCreatedAsync();
            otherTenantMessageId = await new EfCoreOutboxPublisher<OutboxDbContext>(otherTenant)
                .PublishAsync(new OrderPlaced(41));
            await otherTenant.SaveChangesAsync();
        }

        await using (var currentTenant = new OutboxDbContext(options, new TenantContext(73)))
        {
            var currentTenantMessageId = await new EfCoreOutboxPublisher<OutboxDbContext>(currentTenant)
                .PublishAsync(new OrderPlaced(73));
            await currentTenant.SaveChangesAsync();

            var claimed = Assert.Single(await new EfCoreOutboxStore<OutboxDbContext>(currentTenant)
                .ClaimBatchAsync());
            Assert.Equal(currentTenantMessageId, claimed.Id);
            Assert.Equal(73, claimed.TenantId);
        }

        await using var verifyOtherTenant = new OutboxDbContext(options, new TenantContext(41));
        var untouched = await verifyOtherTenant.OutboxMessages.SingleAsync();
        Assert.Equal(otherTenantMessageId, untouched.Id);
        Assert.Equal(OutboxMessageStatus.Pending, untouched.Status);
    }

    [Fact]
    public async Task InboxOutboxDispatcherSkipsDuplicateMessageDelivery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        await new EfCoreOutboxPublisher<OutboxDbContext>(context).PublishAsync(new OrderPlaced(75));
        await context.SaveChangesAsync();
        var message = await context.OutboxMessages.SingleAsync();
        var dispatcher = new TestInboxOutboxDispatcher(
            new EfCoreInboxMessageProcessor<OutboxDbContext>(context), context);

        await dispatcher.DispatchAsync(message);
        await dispatcher.DispatchAsync(message);

        Assert.Equal(1, dispatcher.HandleCalls);
        Assert.Single(await context.Orders.ToListAsync());
        Assert.Single(await context.Set<InboxReceipt>().ToListAsync());
    }

    [Fact]
    public async Task InboxProcessorRunsHandlerOnlyOnceForAConsumerAndMessage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        var processor = new EfCoreInboxMessageProcessor<OutboxDbContext>(context);
        var messageId = Guid.NewGuid();
        var handlerCalls = 0;

        var first = await processor.ProcessOnceAsync("orders-projection", messageId, _ =>
        {
            handlerCalls++;
            context.Orders.Add(new OrderRecord { Id = 73, Name = "processed" });
            return Task.CompletedTask;
        });
        var duplicate = await processor.ProcessOnceAsync("orders-projection", messageId, _ =>
        {
            handlerCalls++;
            return Task.CompletedTask;
        });

        Assert.True(first);
        Assert.False(duplicate);
        Assert.Equal(1, handlerCalls);
        Assert.Single(await context.Orders.ToListAsync());
        Assert.Single(await context.Set<InboxReceipt>().ToListAsync());
    }

    [Fact]
    public async Task InboxProcessorRollsBackReceiptWhenHandlerFails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        var messageId = Guid.Parse("5e211cb4-d252-4af2-b186-21bfa81d7cc5");
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        var processor = new EfCoreInboxMessageProcessor<OutboxDbContext>(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessOnceAsync(
            "orders-projection",
            messageId,
            _ =>
            {
                context.Orders.Add(new OrderRecord { Id = 73, Name = "rolled back" });
                throw new InvalidOperationException("handler failed");
            }));

        var succeeded = await processor.ProcessOnceAsync("orders-projection", messageId, _ =>
        {
            context.Orders.Add(new OrderRecord { Id = 74, Name = "retried" });
            return Task.CompletedTask;
        });

        Assert.True(succeeded);
        Assert.Equal("retried", Assert.Single(await context.Orders.ToListAsync()).Name);
        Assert.Single(await context.Set<InboxReceipt>().ToListAsync());
    }

    [Fact]
    public async Task InboxReceiptDeduplicationIsIsolatedByTenantIncludingHostScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        var messageId = Guid.NewGuid();
        foreach (var tenantId in new long?[] { null, 41, 73 })
        {
            await using var context = new OutboxDbContext(options, new TenantContext(tenantId));
            await context.Database.EnsureCreatedAsync();
            var processor = new EfCoreInboxMessageProcessor<OutboxDbContext>(context);
            Assert.True(await processor.ProcessOnceAsync("same-consumer", messageId, _ => Task.CompletedTask));
            Assert.False(await processor.ProcessOnceAsync("same-consumer", messageId, _ => Task.CompletedTask));
        }

        await using var verify = new OutboxDbContext(options, new TenantContext(73));
        Assert.Equal(3, await verify.Set<InboxReceipt>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ConcurrentInboxDeliveryRunsDatabaseHandlerOnlyOnce()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"inbox-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connectionString).Options;
        await using (var setup = new OutboxDbContext(options, new TenantContext(73)))
            await setup.Database.EnsureCreatedAsync();

        var messageId = Guid.NewGuid();
        var handlerCalls = 0;
        async Task<bool> ProcessInOwnContextAsync(int orderId)
        {
            await using var context = new OutboxDbContext(options, new TenantContext(73));
            var processor = new EfCoreInboxMessageProcessor<OutboxDbContext>(context);
            return await processor.ProcessOnceAsync("orders-projection", messageId, async _ =>
            {
                Interlocked.Increment(ref handlerCalls);
                context.Orders.Add(new OrderRecord { Id = orderId, Name = "concurrent" });
                await Task.Yield();
            });
        }

        var results = await Task.WhenAll(ProcessInOwnContextAsync(81), ProcessInOwnContextAsync(82));

        Assert.Single(results, processed => processed);
        Assert.Single(results, processed => !processed);
        Assert.Equal(1, handlerCalls);
        await using var verify = new OutboxDbContext(options, new TenantContext(73));
        Assert.Single(await verify.Orders.ToListAsync());
        Assert.Single(await verify.Set<InboxReceipt>().ToListAsync());
    }

    [Fact]
    public async Task RollingBackBusinessUnitOfWorkAlsoRollsBackOutboxMessage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Orders.Add(new OrderRecord { Id = 6, Name = "rolled back" });
            await new EfCoreOutboxPublisher<OutboxDbContext>(context).PublishAsync(new OrderPlaced(6));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verify = new OutboxDbContext(options, new TenantContext(73));
        Assert.Empty(await verify.Orders.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.OutboxMessages.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ExpiredLeaseCanBeReclaimedAndOldLeaseTokenCannotCompleteMessage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var outboxOptions = new OutboxOptions { LeaseDuration = TimeSpan.FromMinutes(1) };
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        await new EfCoreOutboxPublisher<OutboxDbContext>(context, outboxOptions, clock)
            .PublishAsync(new OrderPlaced(7));
        await context.SaveChangesAsync();
        var store = new EfCoreOutboxStore<OutboxDbContext>(context, outboxOptions, clock);

        var firstClaim = Assert.Single(await store.ClaimBatchAsync());
        var firstLeaseToken = Assert.IsType<Guid>(firstClaim.LeaseToken);
        clock.Advance(TimeSpan.FromMinutes(1));

        var reclaimed = Assert.Single(await store.ClaimBatchAsync());
        var currentLeaseToken = Assert.IsType<Guid>(reclaimed.LeaseToken);
        Assert.NotEqual(firstLeaseToken, currentLeaseToken);
        Assert.Equal(2, reclaimed.AttemptCount);
        Assert.False(await store.MarkDeliveredAsync(reclaimed.Id, firstLeaseToken));
        Assert.False(await store.MarkFailedAsync(reclaimed.Id, firstLeaseToken, "StaleFailure"));
        Assert.True(await store.MarkDeliveredAsync(reclaimed.Id, currentLeaseToken));

        await using var verify = new OutboxDbContext(options, new TenantContext(73));
        var persisted = await verify.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.Delivered, persisted.Status);
        Assert.NotNull(persisted.DeliveredAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredLeaseTokenCannotCompleteBeforeReclaim(bool markFailed)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var outboxOptions = new OutboxOptions { LeaseDuration = TimeSpan.FromMinutes(1) };
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        await new EfCoreOutboxPublisher<OutboxDbContext>(context, outboxOptions, clock)
            .PublishAsync(new OrderPlaced(70));
        await context.SaveChangesAsync();
        var store = new EfCoreOutboxStore<OutboxDbContext>(context, outboxOptions, clock);

        var claim = Assert.Single(await store.ClaimBatchAsync());
        var leaseToken = Assert.IsType<Guid>(claim.LeaseToken);
        clock.Advance(TimeSpan.FromMinutes(1));

        var completed = markFailed
            ? await store.MarkFailedAsync(claim.Id, leaseToken, "LateFailure")
            : await store.MarkDeliveredAsync(claim.Id, leaseToken);

        Assert.False(completed);
        await using (var verify = new OutboxDbContext(options, new TenantContext(73)))
        {
            var persisted = await verify.OutboxMessages.SingleAsync();
            Assert.Equal(OutboxMessageStatus.Processing, persisted.Status);
            Assert.Equal(leaseToken, persisted.LeaseToken);
        }

        var reclaimed = Assert.Single(await store.ClaimBatchAsync());
        Assert.NotEqual(leaseToken, reclaimed.LeaseToken);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Fact]
    public async Task FailedMessageWaitsForBackoffThenMovesToDeadLetterAtAttemptLimit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var outboxOptions = new OutboxOptions
        {
            MaxAttempts = 2,
            BaseRetryDelay = TimeSpan.FromSeconds(2),
            MaxRetryDelay = TimeSpan.FromSeconds(10)
        };
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        await new EfCoreOutboxPublisher<OutboxDbContext>(context, outboxOptions, clock)
            .PublishAsync(new OrderPlaced(8));
        await context.SaveChangesAsync();
        var store = new EfCoreOutboxStore<OutboxDbContext>(context, outboxOptions, clock);

        var firstClaim = Assert.Single(await store.ClaimBatchAsync());
        Assert.True(await store.MarkFailedAsync(firstClaim.Id, Assert.IsType<Guid>(firstClaim.LeaseToken), "TransientFailure"));
        Assert.Empty(await store.ClaimBatchAsync());
        clock.Advance(TimeSpan.FromSeconds(2));

        var secondClaim = Assert.Single(await store.ClaimBatchAsync());
        Assert.Equal(2, secondClaim.AttemptCount);
        Assert.True(await store.MarkFailedAsync(secondClaim.Id, Assert.IsType<Guid>(secondClaim.LeaseToken), "TransientFailure"));

        await using var verify = new OutboxDbContext(options, new TenantContext(73));
        var persisted = await verify.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.DeadLetter, persisted.Status);
        Assert.Equal("TransientFailure", persisted.LastErrorCode);
        Assert.Null(persisted.LeaseToken);
        Assert.Null(persisted.LeaseExpiresAt);
    }

    [Fact]
    public async Task ExpiredFinalAttemptMovesToDeadLetterWithoutAnotherClaim()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connection).Options;
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var outboxOptions = new OutboxOptions
        {
            MaxAttempts = 1,
            LeaseDuration = TimeSpan.FromMinutes(1)
        };
        await using var context = new OutboxDbContext(options, new TenantContext(73));
        await context.Database.EnsureCreatedAsync();
        await new EfCoreOutboxPublisher<OutboxDbContext>(context, outboxOptions, clock)
            .PublishAsync(new OrderPlaced(9));
        await context.SaveChangesAsync();
        var store = new EfCoreOutboxStore<OutboxDbContext>(context, outboxOptions, clock);

        Assert.Single(await store.ClaimBatchAsync());
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Empty(await store.ClaimBatchAsync());
        await using var verify = new OutboxDbContext(options, new TenantContext(73));
        var persisted = await verify.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.DeadLetter, persisted.Status);
        Assert.Equal("AttemptsExhausted", persisted.LastErrorCode);
        Assert.Null(persisted.LeaseToken);
    }

    private sealed record OrderPlaced(int OrderId);

    private sealed class TestOutboxScopeProvider(IServiceScopeFactory scopeFactory) : IOutboxScopeProvider
    {
        public async IAsyncEnumerable<AsyncServiceScope> CreateScopesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var tenantId in new long?[] { 101, 202 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                var scope = scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<MutableTenantContext>().TenantId = tenantId;
                yield return scope;
            }
        }
    }

    private sealed class RecordingOutboxDispatcher(
        ConcurrentQueue<long?> dispatchedTenants,
        TaskCompletionSource dispatched) : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            dispatchedTenants.Enqueue(message.TenantId);
            if (dispatchedTenants.Count == 2)
                dispatched.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public long? TenantId { get; set; }
    }

    private sealed class TestInboxOutboxDispatcher(
        IInboxMessageProcessor inboxProcessor,
        OutboxDbContext context) : InboxOutboxDispatcher(inboxProcessor)
    {
        public int HandleCalls { get; private set; }

        protected override string ConsumerName => "orders-projection";

        protected override Task HandleMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            HandleCalls++;
            context.Orders.Add(new OrderRecord { Id = message.AttemptCount + 75, Name = "dispatched" });
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class OrderRecord : IMultiTenant
    {
        public int Id { get; set; }
        public long? TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options, ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        public DbSet<OrderRecord> Orders => Set<OrderRecord>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftOutbox();
        }
    }

    private sealed class TenantContext(long? tenantId) : ITenantContext
    {
        public long? TenantId => tenantId;
    }
}