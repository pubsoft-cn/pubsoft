using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken = default);
    Task<bool> MarkDeliveredAsync(long messageId, Guid leaseToken, CancellationToken cancellationToken = default);
    Task<bool> MarkFailedAsync(
        long messageId,
        Guid leaseToken,
        string errorCode,
        CancellationToken cancellationToken = default);
}
