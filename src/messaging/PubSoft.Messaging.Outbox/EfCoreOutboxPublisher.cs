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
    private readonly IOutboxEventSerializer? _eventSerializer;

    /// <summary>Creates the publisher; <paramref name="idGenerator"/> is required (fail-closed: no Guid fallback).</summary>
    public EfCoreOutboxPublisher(
        TDbContext dbContext,
        PubSoft.Core.IIdGenerator idGenerator,
        OutboxOptions? options = null,
        TimeProvider? timeProvider = null,
        IOutboxEventSerializer? eventSerializer = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _options = (options ?? new OutboxOptions()).CloneAndValidate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _eventSerializer = eventSerializer;
    }

    /// <inheritdoc />
    public Task<long> PublishAsync<TEvent>(
        TEvent @event,
        string eventType,
        string? targetModule = null,
        string? aggregateKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeType = @event.GetType();
        var contractKey = ValidateExplicitEventType(eventType);
        var payload = _eventSerializer?.Serialize(@event, runtimeType)
            ?? System.Text.Json.JsonSerializer.Serialize(@event, runtimeType);
        if (string.IsNullOrEmpty(payload))
            throw new InvalidOperationException("The outbox event serializer returned an empty payload.");
        if (payload.Length > _options.MaximumPayloadCharacters)
            throw new ArgumentOutOfRangeException(nameof(@event), "The serialized event payload exceeds the configured limit.");

        var message = new OutboxMessage(
            _idGenerator.NextId(),
            contractKey,
            payload,
            targetModule,
            aggregateKey,
            _timeProvider.GetUtcNow(),
            _options.MaxAttempts,
            correlationId ?? System.Diagnostics.Activity.Current?.TraceId.ToString());
        _dbContext.Set<OutboxMessage>().Add(message);
        return Task.FromResult(message.Id);
    }

    private static string ValidateExplicitEventType(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        if (eventType.Length > 256)
            throw new ArgumentException("Outbox event type must be at most 256 characters.", nameof(eventType));
        if (eventType.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '*' or '#' or '+'))
            throw new ArgumentException("Outbox event type must not contain whitespace, control, or transport wildcard characters.", nameof(eventType));
        if (eventType.StartsWith('$'))
            throw new ArgumentException("Outbox event type must not start with '$'.", nameof(eventType));
        return eventType;
    }

}
