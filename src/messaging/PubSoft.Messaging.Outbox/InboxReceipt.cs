using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public sealed class InboxReceipt : IMultiTenant
{

    internal InboxReceipt(long? tenantId, string consumerName, long messageId, DateTimeOffset processedAt)
    {
        TenantId = tenantId;
        TenantKey = tenantId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "host";
        ConsumerName = consumerName;
        MessageId = messageId;
        ProcessedAt = processedAt;
    }
    private InboxReceipt() { }

    public long? TenantId { get; set; }
    public string TenantKey { get; private set; } = string.Empty;
    public string ConsumerName { get; private set; } = string.Empty;
    public long MessageId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}
