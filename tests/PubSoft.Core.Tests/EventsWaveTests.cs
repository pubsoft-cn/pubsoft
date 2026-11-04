using Microsoft.Extensions.DependencyInjection;
using PubSoft.Events;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EventsWaveTests
{
    private sealed class ProbeEvent : DomainEvent
    {
        public string Tag { get; init; } = "";
    }

    private static IServiceScopeFactory Sp() => new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    [Fact]
    public void DomainEventStampsOccurredOn()
    {
        var e = new ProbeEvent();
        Assert.NotEqual(default, e.OccurredOn);
        Assert.True(e.OccurredOn <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task DispatchAsyncInvokesRegisteredHandler()
    {
        using var d = new DomainEventDispatcher(Sp());
        var hits = 0;
        d.RegisterHandler<ProbeEvent>((e, _) => { hits++; return Task.CompletedTask; });
        await d.DispatchAsync(new ProbeEvent { Tag = "x" });
        Assert.Equal(1, hits);
    }

    [Fact]
    public async Task UnregisteredEventTypeIsNoOp()
    {
        using var d = new DomainEventDispatcher(Sp());
        await d.DispatchAsync(new ProbeEvent()); // no handler — must not throw
    }

    [Fact]
    public async Task PublishAsyncEnqueuesForBackgroundDrain()
    {
        using var d = new DomainEventDispatcher(Sp());
        await d.PublishAsync(new ProbeEvent { Tag = "q" }); // enqueues without throwing
    }

    [Fact]
    public void OptionsCapacityDefault()
        => Assert.Equal(1_000, new DomainEventOptions().Capacity);
}
