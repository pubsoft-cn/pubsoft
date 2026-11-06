using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public interface IInboxMessageProcessor
{
    Task<bool> ProcessOnceAsync(
        string consumerName,
        Guid messageId,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}
