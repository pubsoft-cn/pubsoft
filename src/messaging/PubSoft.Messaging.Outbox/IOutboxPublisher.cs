using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public interface IOutboxPublisher
{
    Task<Guid> PublishAsync<TEvent>(
        TEvent @event,
        string? targetModule = null,
        string? aggregateKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
