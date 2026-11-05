using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboxTailWave2Tests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => 1; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.ConfigurePubSoftOutbox();
        }
    }

    [Fact]
    public void AddPubSoftOutboxRegistersStore()
    {
        var s = new ServiceCollection();
        s.AddDbContext<Ctx>(o => o.UseSqlite("Data Source=:memory:"));
        s.AddPubSoftOutbox<Ctx>();
        Assert.Contains(s, d => d.ServiceType == typeof(IOutboxStore));
        Assert.Contains(s, d => d.ServiceType == typeof(IOutboxPublisher));
    }

    [Fact]
    public void AddPubSoftOutboxWorkerRegistersHosted()
    {
        var s = new ServiceCollection();
        s.AddDbContext<Ctx>(o => o.UseSqlite("Data Source=:memory:"));
        s.AddPubSoftOutbox<Ctx>().AddPubSoftOutboxWorker();
        Assert.Contains(s, d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
    }

    [Fact]
    public void OutboxMessageFullMembers()
    {
        var m = (OutboxMessage)typeof(OutboxMessage)
            .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                new[] { typeof(Guid), typeof(string), typeof(string), typeof(string), typeof(string), typeof(DateTimeOffset), typeof(int), typeof(string) }, null)!
            .Invoke(new object?[] { Guid.NewGuid(), "type", "{}", "mod", "agg", DateTimeOffset.UtcNow, 5, "corr-1" });
        Assert.Equal("mod", m.TargetModule);
        Assert.Equal("agg", m.AggregateKey);
        Assert.Equal("corr-1", m.CorrelationId);
        Assert.Equal(5, m.MaxAttempts);
        Assert.Null(m.NextAttemptAt);
        Assert.Null(m.LeaseExpiresAt);
        Assert.Null(m.LastErrorCode);
        Assert.NotEqual(Guid.Empty, m.Id);
    }

    [Fact]
    public void InboxReceiptMembers()
    {
        var r = (InboxReceipt)typeof(InboxReceipt)
            .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                new[] { typeof(long?), typeof(string), typeof(Guid), typeof(DateTimeOffset) }, null)!
            .Invoke(new object?[] { 7L, "consumer", Guid.NewGuid(), DateTimeOffset.UtcNow });
        Assert.Equal("consumer", r.ConsumerName);
        Assert.Equal(7L, r.TenantId);
        Assert.NotEqual(default, r.ProcessedAt);
        Assert.NotEqual(Guid.Empty, r.MessageId);
    }
}
