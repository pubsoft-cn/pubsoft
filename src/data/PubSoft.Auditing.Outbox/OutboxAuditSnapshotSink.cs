using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Auditing;
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

    /// <inheritdoc />
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
