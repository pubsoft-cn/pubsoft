// README anchor: src/messaging/PubSoft.Messaging.Outbox/README.md — registration example
using Microsoft.Extensions.DependencyInjection;
using PubSoft.DependencyInjection;
using PubSoft.Messaging.Outbox;

namespace PubSoft.DocsSnippets;

/// <summary>Snippet: outbox registration requires the id generator first.</summary>
internal static class MessagingOutboxSnippets
{
    internal static void Register(IServiceCollection services, long workerId)
    {
        // Required: outbox rows use Snowflake long ids assigned client-side
        services.AddPubSoftIdGenerator(workerId);
        services.AddPubSoftOutbox<DocDbContext>(options =>
        {
            options.BatchSize = 50;
            options.MaxAttempts = 5;
        });
        services.AddPubSoftOutboxWorker();
    }
}
