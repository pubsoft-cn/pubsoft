using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Auditing;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Auditing.Outbox;

public sealed record AuditSnapshotOutboxEvent(int SchemaVersion, AuditRequestSnapshot Snapshot);

public sealed class OutboxAuditSnapshotSink<TDbContext>(
    TDbContext dbContext,
    IOutboxPublisher outboxPublisher) : IAuditSnapshotSink
    where TDbContext : PubSoftDbContext
{
    public const string TargetModule = "PubSoft.Auditing";

    public async ValueTask WriteAsync(
        AuditRequestSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await outboxPublisher.PublishAsync(
                new AuditSnapshotOutboxEvent(1, snapshot),
                targetModule: TargetModule,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

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