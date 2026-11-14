using PubSoft.Domain;

namespace PubSoft.Messaging.Outbox;


/// <summary>Inbox dedup receipt: a processed (consumer, messageId) pair.</summary>
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

    /// <summary>Tenant partition; null for host-scope messages.</summary>
    public long? TenantId { get; set; }
    /// <summary>Tenant discriminator segment ("host" when null).</summary>
    public string TenantKey { get; private set; } = string.Empty;
    /// <summary>Consuming component name.</summary>
    public string ConsumerName { get; private set; } = string.Empty;
    /// <summary>Source message identifier.</summary>
    public long MessageId { get; private set; }
    /// <summary>When processing completed.</summary>
    public DateTimeOffset ProcessedAt { get; private set; }
}
