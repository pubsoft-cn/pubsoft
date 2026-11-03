using System.Text.Json;
using PubSoft.Messaging;
using PubSoft.Messaging.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MessagingOutboxWaveTests
{
    [Fact]
    public void MessageEnvelopeRoundtrips()
    {
        var payload = JsonSerializer.SerializeToElement(new { n = 1 });
        var env = new MessageEnvelope("id1", "order.created", payload, DateTimeOffset.UtcNow, "src", "dst");
        Assert.Equal("id1", env.MessageId);
        Assert.Equal("order.created", env.MessageType);
        Assert.Equal(1, env.Payload.GetProperty("n").GetInt32());
        Assert.Equal("src", env.Source);
        Assert.Equal("dst", env.Target);
    }

    [Fact]
    public void AddPubSoftOutboxRegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddPubSoftOutbox<FakeDbContext>();
        Assert.Contains(services, d => d.ServiceType == typeof(IOutboxPublisher));
        Assert.Contains(services, d => d.ServiceType == typeof(IOutboxStore));
        Assert.Contains(services, d => d.ServiceType == typeof(IInboxMessageProcessor));
        services.AddPubSoftOutboxWorker();
        Assert.Contains(services, d => d.ImplementationType == typeof(OutboxBackgroundService));
    }

    [Fact]
    public void SurfaceTypesExist()
    {
        Assert.NotNull(typeof(EfCoreOutboxStore<>));
        Assert.NotNull(typeof(EfCoreOutboxPublisher<>));
        Assert.NotNull(typeof(EfCoreInboxMessageProcessor<>));
        Assert.NotNull(typeof(InboxOutboxDispatcher));
        Assert.NotNull(typeof(OutboxBackgroundService));
        Assert.NotNull(typeof(InboxRetentionCleanup<>));
        Assert.NotNull(typeof(IOutboxScopeProvider));
        Assert.NotNull(typeof(IOutboxDispatcher));
        Assert.NotNull(typeof(MessageDispatcher));
        Assert.NotNull(typeof(IMessagePublisher));
        Assert.NotNull(typeof(OutboxMessage));
    }

    private sealed record StubTenantContext(long? TenantId) : PubSoft.Domain.ITenantContext;

    private sealed class FakeDbContext : PubSoft.EntityFrameworkCore.PubSoftDbContext
    {
        public FakeDbContext(Microsoft.EntityFrameworkCore.DbContextOptions options)
            : base(options, new StubTenantContext(null), null, null, null) { }
    }
}
