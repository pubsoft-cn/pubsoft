using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


/// <summary>EF-backed outbox publisher writing rows inside the ambient DbContext transaction.</summary>
public sealed class EfCoreOutboxPublisher<TDbContext> : IOutboxPublisher
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly PubSoft.Core.IIdGenerator _idGenerator;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>Creates the publisher; <paramref name="idGenerator"/> is required (fail-closed: no Guid fallback).</summary>
    public EfCoreOutboxPublisher(TDbContext dbContext, PubSoft.Core.IIdGenerator idGenerator, OutboxOptions? options = null, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _options = (options ?? new OutboxOptions()).CloneAndValidate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<long> PublishAsync<TEvent>(
        TEvent @event,
        string? targetModule = null,
        string? aggregateKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        var eventType = @event.GetType();
        var payload = System.Text.Json.JsonSerializer.Serialize(@event, eventType);
        if (payload.Length > _options.MaximumPayloadCharacters)
            throw new ArgumentOutOfRangeException(nameof(@event), "The serialized event payload exceeds the configured limit.");

        var message = new OutboxMessage(
            _idGenerator.NextId(),
            eventType.FullName ?? eventType.Name,
            payload,
            targetModule,
            aggregateKey,
            _timeProvider.GetUtcNow(),
            _options.MaxAttempts,
            correlationId ?? System.Diagnostics.Activity.Current?.TraceId.ToString());
        _dbContext.Set<OutboxMessage>().Add(message);
        return Task.FromResult(message.Id);
    }
}
