using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Events;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DomainEventsTests
{
    [Fact]
    public async Task DispatchRunsManualAndScopedHandlersInOrder()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddScoped<IDomainEventHandler<TestEvent>>(_ => new RecordingHandler(calls));
        await using var provider = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(provider.GetRequiredService<IServiceScopeFactory>());
        dispatcher.RegisterHandler<TestEvent>((_, _) =>
        {
            calls.Add("manual");
            return Task.CompletedTask;
        });

        await dispatcher.DispatchAsync(new TestEvent());

        Assert.Equal(["manual", "scoped"], calls);
    }

    [Fact]
    public async Task SynchronousDispatchPropagatesHandlerFailure()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDomainEventHandler<TestEvent>, FailingHandler>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(provider.GetRequiredService<IServiceScopeFactory>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new TestEvent()));

        Assert.Equal("handler failed", exception.Message);
    }

    [Fact]
    public async Task PublishAppliesBoundedBackpressureAndHonorsCancellation()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        using var dispatcher = new DomainEventDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            new DomainEventOptions { Capacity = 1 });
        await dispatcher.PublishAsync(new TestEvent());
        using var cancellation = new CancellationTokenSource();
        var blockedWrite = dispatcher.PublishAsync(new TestEvent(), cancellation.Token);

        Assert.False(blockedWrite.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockedWrite);
    }

    [Fact]
    public async Task BackgroundPublishContinuesAfterOneHandlerFails()
    {
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.AddScoped<IDomainEventHandler<TestEvent>, FailingHandler>();
        services.AddScoped<IDomainEventHandler<TestEvent>, SignalingHandler>();
        services.AddPubSoftEvents(options => options.Capacity = 2);
        await using var provider = services.BuildServiceProvider();
        var hostedService = Assert.Single(provider.GetServices<IHostedService>());

        await hostedService.StartAsync(CancellationToken.None);
        await provider.GetRequiredService<IDomainEventDispatcher>().PublishAsync(new TestEvent());
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void EventCapacityIsValidated()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        Assert.Throws<ArgumentOutOfRangeException>(() => new DomainEventDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            new DomainEventOptions { Capacity = 0 }));
    }

    private sealed class TestEvent : DomainEvent;

    private sealed class RecordingHandler(List<string> calls) : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            calls.Add("scoped");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("handler failed");
    }

    private sealed class SignalingHandler(TaskCompletionSource<bool> received) : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            received.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}