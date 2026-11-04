using Microsoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;
using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboxWaveTests
{
    private sealed class Ctx(DbContextOptions<Ctx> o) : DbContext(o)
    {
        protected override void OnModelCreating(ModelBuilder b) => b.ConfigurePubSoftOutbox();
    }

    [Fact]
    public void OutboxMessageStatusEnumValues()
    {
        Assert.True(Enum.IsDefined(OutboxMessageStatus.Pending));
        Assert.True(Enum.IsDefined(OutboxMessageStatus.Delivered));
        Assert.True(Enum.GetValues<OutboxMessageStatus>().Length >= 3);
    }

    [Fact]
    public void ConfigurePubSoftOutboxRegistersEntities()
    {
        var opts = new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options;
        using var ctx = new Ctx(opts);
        var msg = ctx.Model.FindEntityType(typeof(OutboxMessage));
        Assert.NotNull(msg);
        Assert.NotNull(msg!.FindProperty(nameof(OutboxMessage.EventType)));
        Assert.Equal(512, msg.FindProperty(nameof(OutboxMessage.EventType))!.GetMaxLength());
        Assert.True(msg.FindProperty(nameof(OutboxMessage.PayloadJson))!.IsNullable == false);
        var receipt = ctx.Model.FindEntityType(typeof(InboxReceipt));
        Assert.NotNull(receipt);
    }

    [Fact]
    public void OutboxMessageTenantScoped()
        => Assert.True(typeof(IMultiTenant).IsAssignableFrom(typeof(OutboxMessage)));

    [Fact]
    public void InboxReceiptTenantScoped()
        => Assert.True(typeof(IMultiTenant).IsAssignableFrom(typeof(InboxReceipt)));
}
