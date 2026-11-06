using Microsoft.Extensions.Hosting;

namespace PubSoft.Events;

public sealed class DomainEventBackgroundService(DomainEventDispatcher dispatcher) : BackgroundService
{

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        dispatcher.CompleteWriter();
        return base.StopAsync(cancellationToken);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var @event in dispatcher.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                await dispatcher.DispatchPublishedAsync(@event, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}