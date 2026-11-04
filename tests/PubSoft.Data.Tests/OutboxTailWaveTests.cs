using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboxTailWaveTests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => 1; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        public DbSet<OutboxMessage> Messages => Set<OutboxMessage>();
        public DbSet<InboxReceipt> Receipts => Set<InboxReceipt>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.ConfigurePubSoftOutbox();
        }
    }

    private static Ctx Db()
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection();
        c.Database.EnsureCreated();
        return c;
    }

    [Fact]
    public async Task PublisherPersistsPendingMessage()
    {
        using var ctx = Db();
        var pub = new EfCoreOutboxPublisher<Ctx>(ctx);
        var id = await pub.PublishAsync(new { orderId = 1 });
        await ctx.SaveChangesAsync();
        var saved = await ctx.Messages.FindAsync(id);
        Assert.NotNull(saved);
        Assert.Equal(OutboxMessageStatus.Pending, saved!.Status);
        Assert.Equal(0, saved.AttemptCount);
        Assert.NotEqual(default, saved.OccurredAt);
        Assert.Null(saved.DeliveredAt);
        Assert.Null(saved.LeaseToken);
    }

    private sealed class OrderEvent { public long OrderId { get; init; } }

    [Fact]
    public async Task PublisherSerializesEventType()
    {
        using var ctx = Db();
        var pub = new EfCoreOutboxPublisher<Ctx>(ctx);
        var id = await pub.PublishAsync(new OrderEvent { OrderId = 9 });
        await ctx.SaveChangesAsync();
        var saved = await ctx.Messages.FindAsync(id);
        Assert.NotNull(saved!.EventType);
        Assert.Contains("OrderEvent", saved.EventType);
        Assert.Contains("OrderId", saved.PayloadJson); // default serializer keeps PascalCase
    }

    [Fact]
    public async Task InboxRetentionCleanupDeletesOldReceipts()
    {
        using var ctx = Db();
        var cleanup = new InboxRetentionCleanup<Ctx>(ctx);
        var removed = await cleanup.DeleteProcessedBeforeAsync(TimeSpan.FromDays(1));
        Assert.Equal(0, removed); // empty table → nothing removed
    }
}
