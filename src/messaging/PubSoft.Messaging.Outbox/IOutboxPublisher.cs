using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


/// <summary>Enqueues an event into the transactional outbox inside the ambient DbContext transaction.</summary>
public interface IOutboxPublisher
{
    /// <summary>Writes the event; returns the Snowflake message id. Correlation falls back to the ambient Activity trace id.</summary>
    Task<long> PublishAsync<TEvent>(
        TEvent @event,
        string? targetModule = null,
        string? aggregateKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
