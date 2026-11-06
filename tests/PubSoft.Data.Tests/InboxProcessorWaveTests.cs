using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class InboxProcessorWaveTests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => 1; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
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
    public async Task ProcessOnceInsertsReceiptAndRunsHandler()
    {
        using var ctx = Db();
        var p = new EfCoreInboxMessageProcessor<Ctx>(ctx);
        var id = PubSoft.Testing.TestIds.Next();
        var ran = 0;
        Assert.True(await p.ProcessOnceAsync("c1", id, _ => { ran++; return Task.CompletedTask; }));
        Assert.Equal(1, ran);
        Assert.Equal(1, await ctx.Receipts.CountAsync());
    }

    [Fact]
    public async Task DuplicateMessageIdSkipsHandler()
    {
        using var ctx = Db();
        var p = new EfCoreInboxMessageProcessor<Ctx>(ctx);
        var id = PubSoft.Testing.TestIds.Next();
        var ran = 0;
        await p.ProcessOnceAsync("c1", id, _ => { ran++; return Task.CompletedTask; });
        Assert.False(await p.ProcessOnceAsync("c1", id, _ => { ran++; return Task.CompletedTask; })); // dedup
        Assert.Equal(1, ran); // handler ran once only
    }

    [Fact]
    public async Task DedupIsPerConsumer()
    {
        using var ctx = Db();
        var p = new EfCoreInboxMessageProcessor<Ctx>(ctx);
        var id = PubSoft.Testing.TestIds.Next();
        Assert.True(await p.ProcessOnceAsync("c1", id, _ => Task.CompletedTask));
        Assert.True(await p.ProcessOnceAsync("c2", id, _ => Task.CompletedTask)); // different consumer → new receipt
        Assert.Equal(2, await ctx.Receipts.CountAsync());
    }

    [Fact]
    public async Task InvalidArgsRejected()
    {
        using var ctx = Db();
        var p = new EfCoreInboxMessageProcessor<Ctx>(ctx);
        await Assert.ThrowsAsync<ArgumentException>(() => p.ProcessOnceAsync("c", 0L, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(() => p.ProcessOnceAsync("c", PubSoft.Testing.TestIds.Next(), null!));
        await Assert.ThrowsAsync<ArgumentException>(() => p.ProcessOnceAsync(" ", PubSoft.Testing.TestIds.Next(), _ => Task.CompletedTask));
    }
}
