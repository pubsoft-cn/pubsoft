using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public sealed class OutboxMessage : IMultiTenant
{

    internal OutboxMessage(
        Guid id,
        string eventType,
        string payloadJson,
        string? targetModule,
        string? aggregateKey,
        DateTimeOffset occurredAt,
        int maxAttempts,
        string? correlationId = null)
    {
        // 列上限对齐 OnModelCreating——超长原样入库会整批 SaveChanges 失败，宁可截断留痕
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

    public Guid Id { get; private set; }
    public long? TenantId { get; set; }
    public string EventType { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public string? TargetModule { get; private set; }
    public string? AggregateKey { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? LastErrorCode { get; private set; }

    /// <summary>
    /// Correlation id propagated from the publishing context — explicit
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
