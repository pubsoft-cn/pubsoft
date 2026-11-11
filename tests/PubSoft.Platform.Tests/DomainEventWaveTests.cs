using Microsoft.Extensions.DependencyInjection;
using PubSoft.Events;
using Xunit;

namespace PubSoft.Platform.Tests;

/// <summary>Generic domain-event registration + dispatch coverage.</summary>
public sealed class DomainEventWaveTests
{
    private sealed class Ping : IDomainEvent { public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow; }
    private sealed class PingHandler : IDomainEventHandler<Ping>
    {
        public static int Count;
        public Task HandleAsync(Ping @event, CancellationToken cancellationToken = default)
        { Count++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task GenericHandlerRegistersAndDispatches()
    {
        PingHandler.Count = 0;
        var sp = new ServiceCollection()
            .AddPubSoftEvents()
            .AddPubSoftDomainEventHandler<Ping, PingHandler>()
            .BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<DomainEventDispatcher>();
        await dispatcher.DispatchAsync(new Ping());
        Assert.Equal(1, PingHandler.Count);
    }
}
