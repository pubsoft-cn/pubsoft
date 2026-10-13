using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Events;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddPubSoftEvents(options => options.Capacity = 4);
var handler = new SmokeEventHandler();
builder.Services.AddSingleton<IDomainEventHandler<SmokeEvent>>(handler);

using var host = builder.Build();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await host.StartAsync(timeout.Token);

var dispatcher = host.Services.GetRequiredService<IDomainEventDispatcher>();
await dispatcher.PublishAsync(new SmokeEvent("queued"), timeout.Token);
var handledValue = await handler.Handled.Task.WaitAsync(timeout.Token);
if (handledValue != "queued")
    throw new InvalidOperationException("Events asynchronous dispatch smoke check failed.");

await host.StopAsync(timeout.Token);
Console.WriteLine("Isolated PubSoft.Events consumer smoke check passed.");

sealed class SmokeEvent(string value) : DomainEvent
{
    public string Value { get; } = value;
}

sealed class SmokeEventHandler : IDomainEventHandler<SmokeEvent>
{
    public TaskCompletionSource<string> Handled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task HandleAsync(SmokeEvent @event, CancellationToken cancellationToken = default)
    {
        Handled.TrySetResult(@event.Value);
        return Task.CompletedTask;
    }
}