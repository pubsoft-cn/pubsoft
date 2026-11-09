using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


/// <summary>Persisted outbox row: at-least-once delivery envelope with lease/retry/dead-letter bookkeeping.</summary>
public sealed class OutboxMessage : IMultiTenant
{

    internal OutboxMessage(
        long id,
        string eventType,
        string payloadJson,
        string? targetModule,
        string? aggregateKey,
        DateTimeOffset occurredAt,
        int maxAttempts,
        string? correlationId = null)
    {
        // 列上限对齐 OnModelCreating--超长原样入库会整批 SaveChanges 失败，宁可截断留痕
        Id = id;
        EventType = Truncate(eventType, 512)!;
        PayloadJson = payloadJson;
        TargetModule = Truncate(targetModule, 128);
        AggregateKey = Truncate(aggregateKey, 256);
        CorrelationId = Truncate(correlationId, 128);
        OccurredAt = occurredAt;
        Status = OutboxMessageStatus.Pending;
        MaxAttempts = maxAttempts;
    }
    private OutboxMessage() { }

    /// <summary>Snowflake id assigned at publish (IIdGenerator).</summary>
    public long Id { get; private set; }
    /// <summary>Tenant partition; null for host-scope events.</summary>
    public long? TenantId { get; set; }
    /// <summary>Event type discriminator (truncated to 512 chars at publish).</summary>
    public string EventType { get; private set; } = string.Empty;
    /// <summary>Serialized event payload.</summary>
    public string PayloadJson { get; private set; } = string.Empty;
    /// <summary>Target module hint for routing (truncated to 128 chars).</summary>
    public string? TargetModule { get; private set; }
    /// <summary>Aggregate key for ordering/compaction hints (truncated to 256 chars).</summary>
    public string? AggregateKey { get; private set; }
    /// <summary>When the domain event occurred.</summary>
    public DateTimeOffset OccurredAt { get; private set; }
    /// <summary>Delivery state.</summary>
    public OutboxMessageStatus Status { get; private set; }
    /// <summary>Delivery attempts taken so far.</summary>
    public int AttemptCount { get; private set; }
    /// <summary>Attempts allowed before dead-lettering.</summary>
    public int MaxAttempts { get; private set; }
    /// <summary>Earliest retry time after a failed attempt.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }
    /// <summary>Fencing token of the active processing lease.</summary>
    public Guid? LeaseToken { get; private set; }
    /// <summary>Lease expiry; lapsed leases are reclaimable by other workers.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    /// <summary>When the message was dispatched (null until delivered).</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }
    /// <summary>Error code from the last failed attempt.</summary>
    public string? LastErrorCode { get; private set; }

    /// <summary>
    /// Correlation id propagated from the publishing context -- explicit
    /// <see cref="IOutboxPublisher.PublishAsync{TEvent}"/> argument wins, otherwise
    /// the ambient <see cref="System.Diagnostics.Activity.TraceId"/> is used.
    /// Hosts re-emit this value when dispatching so traces survive the bus hop.
    /// </summary>
    public string? CorrelationId { get; private set; }

    internal void AcquireLease(Guid leaseToken, DateTimeOffset now, TimeSpan leaseDuration)
    {
        Status = OutboxMessageStatus.Processing;
        AttemptCount++;
        LeaseToken = leaseToken;
        LeaseExpiresAt = now + leaseDuration;
        NextAttemptAt = null;
    }

    internal void MarkDeadLetter(string errorCode)
    {
        Status = OutboxMessageStatus.DeadLetter;
        LeaseToken = null;
        LeaseExpiresAt = null;
        NextAttemptAt = null;
        LastErrorCode = errorCode;
    }

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
