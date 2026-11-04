using Microsoft.EntityFrameworkCore;
using PubSoft.Auditing;
using PubSoft.Auditing.Outbox;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboxAuditSinkWaveTests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => 1; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        public DbSet<OutboxMessage> Messages => Set<OutboxMessage>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.ConfigurePubSoftOutbox();
        }
    }

    [Fact]
    public async Task WritePublishesSnapshotToOutbox()
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        var sink = new OutboxAuditSnapshotSink<Ctx>(c, new EfCoreOutboxPublisher<Ctx>(c));
        var snap = new AuditRequestSnapshot(
            new AuditRequestContext(DateTimeOffset.UtcNow, "POST", "/api/orders"),
            new AuditBusinessContext("order", "created", "order-1", null),
            Success: true, StatusCode: 200, FailureCode: null, Duration: TimeSpan.FromMilliseconds(50),
            Changes: []);
        await sink.WriteAsync(snap);
        var msg = Assert.Single(await c.Messages.ToListAsync());
        Assert.Equal(OutboxAuditSnapshotSink<Ctx>.TargetModule, msg.TargetModule);
        Assert.Contains("AuditSnapshotOutboxEvent", msg.EventType!);
        Assert.Contains("/api/orders", msg.PayloadJson); // snapshot serialized into payload
        Assert.Equal(OutboxMessageStatus.Pending, msg.Status);
    }

    [Fact]
    public async Task NullSnapshotRejected()
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        var sink = new OutboxAuditSnapshotSink<Ctx>(c, new EfCoreOutboxPublisher<Ctx>(c));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.WriteAsync(null!).AsTask());
    }
}
