using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public sealed class OutboxBackgroundService(
    IServiceScopeFactory scopeFactory,
    OutboxOptions options,
    ILogger<OutboxBackgroundService>? logger = null,
    IOutboxScopeProvider? outboxScopeProvider = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delivered = 0;
            try
            {
                if (outboxScopeProvider is null)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                    delivered = await processor.ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    await foreach (var tenantScope in outboxScopeProvider
                        .CreateScopesAsync(stoppingToken)
                        .WithCancellation(stoppingToken).ConfigureAwait(false)
                        )
                    {
                        await using var scope = tenantScope;
                        try
                        {
                            var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                            delivered += await processor.ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            logger?.LogWarning(
                                "Outbox tenant scope iteration failed ({ExceptionType}).",
                                exception.GetType().Name);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger?.LogWarning("Outbox worker iteration failed ({ExceptionType}).", exception.GetType().Name);
            }

            if (delivered == 0)
            {
                try
                {
                    await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
