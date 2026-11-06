using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public sealed class EfCoreOutboxPublisher<TDbContext> : IOutboxPublisher
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public EfCoreOutboxPublisher(TDbContext dbContext, OutboxOptions? options = null, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = (options ?? new OutboxOptions()).CloneAndValidate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<Guid> PublishAsync<TEvent>(
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
            Guid.NewGuid(),
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
