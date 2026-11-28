using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Auditing.Outbox;


/// <summary><see cref="IAuditSnapshotSink"/> that publishes snapshots as outbox events.</summary>
public sealed class OutboxAuditSnapshotSink<TDbContext>(
    TDbContext dbContext,
    IOutboxPublisher outboxPublisher) : IAuditSnapshotSink
    where TDbContext : PubSoftDbContext
{
    /// <summary>Outbox target module name.</summary>
    public const string TargetModule = "PubSoft.Auditing";

    /// <summary>Stable versioned outbox contract key for audit snapshot events.</summary>
    public const string SnapshotEventType = "pubsoft.auditing.snapshot.v1";

    /// <inheritdoc />
    public async ValueTask WriteAsync(
        AuditRequestSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await outboxPublisher.PublishAsync(
                new AuditSnapshotOutboxEvent(1, snapshot),
                targetModule: TargetModule,
                eventType: SnapshotEventType,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
