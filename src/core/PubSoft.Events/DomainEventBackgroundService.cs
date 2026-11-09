using Microsoft.Extensions.Hosting;

namespace PubSoft.Events;

/// <summary>Drains the dispatch channel and invokes handlers for published events.</summary>
public sealed class DomainEventBackgroundService(DomainEventDispatcher dispatcher) : BackgroundService
{

    /// <inheritdoc />
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) // pubsoft:exempt PUBSOFT041 "graceful shutdown — cancellation is expected, not an error"
        {
        }
    }
}