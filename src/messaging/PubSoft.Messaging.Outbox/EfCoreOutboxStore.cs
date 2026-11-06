using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public sealed class EfCoreOutboxStore<TDbContext> : IOutboxStore
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

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
