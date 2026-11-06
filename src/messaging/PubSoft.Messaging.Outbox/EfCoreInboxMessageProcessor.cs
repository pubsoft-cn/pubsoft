using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public sealed class EfCoreInboxMessageProcessor<TDbContext> : IInboxMessageProcessor
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public EfCoreInboxMessageProcessor(TDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> ProcessOnceAsync(
        string consumerName,
        Guid messageId,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ValidateConsumerName(consumerName);
        ArgumentNullException.ThrowIfNull(handler);
        if (messageId == Guid.Empty)
            throw new ArgumentException("Message ids must not be empty.", nameof(messageId));
        cancellationToken.ThrowIfCancellationRequested();
        if (_dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Inbox processing requires a DbContext with no pending changes.");

        var tenantId = _dbContext.CurrentTenantId;
        var tenantKey = tenantId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "host";
        var receipts = _dbContext.Set<InboxReceipt>();
        if (await receipts.AnyAsync(receipt => receipt.TenantKey == tenantKey // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison — == is the translatable form"
                && receipt.ConsumerName == consumerName // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison — == is the translatable form"
                && receipt.MessageId == messageId, cancellationToken).ConfigureAwait(false)
            )
            return false;

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var receipt = new InboxReceipt(tenantId, consumerName, messageId, _timeProvider.GetUtcNow());
        receipts.Add(receipt);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await transaction.DisposeAsync().ConfigureAwait(false);
            if (await receipts.AnyAsync(existing => existing.TenantKey == tenantKey // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison — == is the translatable form"
                    && existing.ConsumerName == consumerName // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison — == is the translatable form"
                    && existing.MessageId == messageId, cancellationToken).ConfigureAwait(false)
                )
                return false;

            throw;
        }

        try
        {
            await handler(cancellationToken).ConfigureAwait(false);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static void ValidateConsumerName(string consumerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        if (consumerName.Length > 128 || consumerName.Any(char.IsControl))
            throw new ArgumentException("Consumer names must be at most 128 non-control characters.", nameof(consumerName));
    }
}
