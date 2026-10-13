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
    Task<bool> MarkDeliveredAsync(Guid messageId, Guid leaseToken, CancellationToken cancellationToken = default);
    Task<bool> MarkFailedAsync(
        Guid messageId,
        Guid leaseToken,
        string errorCode,
        CancellationToken cancellationToken = default);
}

public interface IOutboxDispatcher
{
    Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

public interface IOutboxScopeProvider
{
    IAsyncEnumerable<AsyncServiceScope> CreateScopesAsync(CancellationToken cancellationToken = default);
}

public abstract class InboxOutboxDispatcher(IInboxMessageProcessor inboxProcessor) : IOutboxDispatcher
{
    private readonly IInboxMessageProcessor _inboxProcessor = inboxProcessor
        ?? throw new ArgumentNullException(nameof(inboxProcessor));

    protected abstract string ConsumerName { get; }

    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await _inboxProcessor.ProcessOnceAsync(
            ConsumerName,
            message.Id,
            token => HandleMessageAsync(message, token),
            cancellationToken).ConfigureAwait(false);
    }

    protected abstract Task HandleMessageAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed class EfCoreInboxMessageProcessor<TDbContext> : IInboxMessageProcessor
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

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
        if (await receipts.AnyAsync(receipt => receipt.TenantKey == tenantKey
                && receipt.ConsumerName == consumerName
                && receipt.MessageId == messageId, cancellationToken)
            .ConfigureAwait(false))
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
            if (await receipts.AnyAsync(existing => existing.TenantKey == tenantKey
                    && existing.ConsumerName == consumerName
                    && existing.MessageId == messageId, cancellationToken)
                .ConfigureAwait(false))
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

public sealed class EfCoreOutboxStore<TDbContext> : IOutboxStore
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;

    public EfCoreOutboxStore(TDbContext dbContext, OutboxOptions? options = null, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = (options ?? new OutboxOptions()).CloneAndValidate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var messages = _dbContext.Set<OutboxMessage>();
        var exhaustedLeaseIds = await messages
            .Where(message => message.Status == OutboxMessageStatus.Processing
                && message.LeaseExpiresAt != null
                && message.LeaseExpiresAt <= now
                && message.AttemptCount >= message.MaxAttempts)
            .OrderBy(message => message.LeaseExpiresAt)
            .ThenBy(message => message.Id)
            .Take(_options.BatchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var messageId in exhaustedLeaseIds)
        {
            await messages
                .Where(message => message.Id == messageId
                    && message.Status == OutboxMessageStatus.Processing
                    && message.LeaseExpiresAt != null
                    && message.LeaseExpiresAt <= now
                    && message.AttemptCount >= message.MaxAttempts)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(message => message.Status, OutboxMessageStatus.DeadLetter)
                    .SetProperty(message => message.LeaseToken, (Guid?)null)
                    .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(message => message.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(message => message.LastErrorCode, "AttemptsExhausted"), cancellationToken)
                .ConfigureAwait(false);
        }

        var candidateIds = await messages
            .Where(message => message.Status == OutboxMessageStatus.Pending
                || (message.Status == OutboxMessageStatus.RetryScheduled
                    && message.NextAttemptAt != null && message.NextAttemptAt <= now)
                || (message.Status == OutboxMessageStatus.Processing
                    && message.LeaseExpiresAt != null && message.LeaseExpiresAt <= now))
            .Where(message => message.AttemptCount < message.MaxAttempts)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .Take(_options.BatchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var claimedIds = new List<Guid>(candidateIds.Count);
        foreach (var messageId in candidateIds)
        {
            var leaseToken = Guid.NewGuid();
            var leaseExpiresAt = now + _options.LeaseDuration;
            var updated = await messages
                .Where(message => message.Id == messageId
                    && message.AttemptCount < message.MaxAttempts
                    && (message.Status == OutboxMessageStatus.Pending
                        || (message.Status == OutboxMessageStatus.RetryScheduled
                            && message.NextAttemptAt != null && message.NextAttemptAt <= now)
                        || (message.Status == OutboxMessageStatus.Processing
                            && message.LeaseExpiresAt != null && message.LeaseExpiresAt <= now)))
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(message => message.Status, OutboxMessageStatus.Processing)
                    .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1)
                    .SetProperty(message => message.LeaseToken, (Guid?)leaseToken)
                    .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?)leaseExpiresAt)
                    .SetProperty(message => message.NextAttemptAt, (DateTimeOffset?)null), cancellationToken)
                .ConfigureAwait(false);
            if (updated == 1)
                claimedIds.Add(messageId);
        }

        var claimedMessages = candidateIds.Count == 0 || claimedIds.Count == 0
            ? []
            : await messages.AsNoTracking()
                .Where(message => claimedIds.Contains(message.Id))
                .OrderBy(message => message.OccurredAt)
                .ThenBy(message => message.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        foreach (var message in claimedMessages)
            _dbContext.Entry(message).State = EntityState.Detached;
        return claimedMessages;
    }

    public async Task<bool> MarkDeliveredAsync(
        Guid messageId,
        Guid leaseToken,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var updated = await _dbContext.Set<OutboxMessage>()
            .Where(message => message.Id == messageId
                && message.Status == OutboxMessageStatus.Processing
                && message.LeaseToken == leaseToken
                && message.LeaseExpiresAt != null
                && message.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(message => message.Status, OutboxMessageStatus.Delivered)
                .SetProperty(message => message.DeliveredAt, (DateTimeOffset?)now)
                .SetProperty(message => message.LeaseToken, (Guid?)null)
                .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(message => message.LastErrorCode, (string?)null), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> MarkFailedAsync(
        Guid messageId,
        Guid leaseToken,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        ValidateErrorCode(errorCode);
        var now = _timeProvider.GetUtcNow();
        var attempt = await _dbContext.Set<OutboxMessage>()
            .Where(message => message.Id == messageId
                && message.Status == OutboxMessageStatus.Processing
                && message.LeaseToken == leaseToken
                && message.LeaseExpiresAt != null
                && message.LeaseExpiresAt > now)
            .Select(message => new { message.AttemptCount, message.MaxAttempts })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (attempt is null)
            return false;

        now = _timeProvider.GetUtcNow();
        var deadLetter = attempt.AttemptCount >= attempt.MaxAttempts;
        var nextAttempt = deadLetter ? (DateTimeOffset?)null : now + GetRetryDelay(attempt.AttemptCount);
        var updated = await _dbContext.Set<OutboxMessage>()
            .Where(message => message.Id == messageId
                && message.Status == OutboxMessageStatus.Processing
                && message.LeaseToken == leaseToken
                && message.LeaseExpiresAt != null
                && message.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(message => message.Status,
                    deadLetter ? OutboxMessageStatus.DeadLetter : OutboxMessageStatus.RetryScheduled)
                .SetProperty(message => message.NextAttemptAt, nextAttempt)
                .SetProperty(message => message.LeaseToken, (Guid?)null)
                .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(message => message.LastErrorCode, errorCode), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1;
    }

    private TimeSpan GetRetryDelay(int attemptCount)
    {
        var multiplier = Math.Pow(2, Math.Min(attemptCount - 1, 30));
        var ticks = Math.Min(_options.MaxRetryDelay.Ticks, _options.BaseRetryDelay.Ticks * multiplier);
        return TimeSpan.FromTicks((long)ticks);
    }

    private static void ValidateErrorCode(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (errorCode.Length > 128 || errorCode.Any(char.IsControl))
            throw new ArgumentException("Error codes must be at most 128 non-control characters.", nameof(errorCode));
    }
}

public sealed class OutboxProcessor(
    IOutboxStore store,
    IOutboxDispatcher dispatcher,
    ILogger<OutboxProcessor>? logger = null)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var messages = await store.ClaimBatchAsync(cancellationToken).ConfigureAwait(false);
        var delivered = 0;
        foreach (var message in messages)
        {
            var leaseToken = message.LeaseToken
                ?? throw new InvalidOperationException("Claimed outbox messages must have a lease token.");
            try
            {
                await dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
                if (await store.MarkDeliveredAsync(message.Id, leaseToken, cancellationToken).ConfigureAwait(false))
                    delivered++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var errorCode = exception.GetType().Name;
                await store.MarkFailedAsync(message.Id, leaseToken, errorCode, cancellationToken).ConfigureAwait(false);
                logger?.LogWarning("Outbox dispatch failed ({ExceptionType}).", errorCode);
            }
        }

        return delivered;
    }
}

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
                        .WithCancellation(stoppingToken)
                        .ConfigureAwait(false))
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