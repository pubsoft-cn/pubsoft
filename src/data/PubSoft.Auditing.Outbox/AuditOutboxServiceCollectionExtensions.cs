using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Auditing;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Auditing.Outbox;


public static class AuditOutboxServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftAuditOutbox<TDbContext>(this IServiceCollection services)
        where TDbContext : PubSoftDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<IAuditSnapshotSink>();
        services.AddScoped<IAuditSnapshotSink, OutboxAuditSnapshotSink<TDbContext>>();
        return services;
    }
}
