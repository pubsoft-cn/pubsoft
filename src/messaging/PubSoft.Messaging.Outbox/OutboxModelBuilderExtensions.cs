using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public static class OutboxModelBuilderExtensions
{
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter = new(
        instant => instant.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    public static ModelBuilder ConfigurePubSoftOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(ConfigureOutboxMessage);
        modelBuilder.Entity<InboxReceipt>(ConfigureInboxReceipt);
        return modelBuilder;
    }

    private static void ConfigureOutboxMessage(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("PubSoftOutboxMessages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.HasIndex(message => new { message.TenantId, message.Status, message.OccurredAt });
        builder.HasIndex(message => new { message.TenantId, message.AggregateKey, message.OccurredAt });
        builder.Property(message => message.OccurredAt).HasConversion(UtcTicksConverter);
        builder.Property(message => message.NextAttemptAt).HasConversion(UtcTicksConverter);
        builder.Property(message => message.LeaseExpiresAt).HasConversion(UtcTicksConverter);
        builder.Property(message => message.DeliveredAt).HasConversion(UtcTicksConverter);
        builder.Property(message => message.EventType).HasMaxLength(512).IsRequired();
        builder.Property(message => message.PayloadJson).IsRequired();
        builder.Property(message => message.TargetModule).HasMaxLength(128);
        builder.Property(message => message.AggregateKey).HasMaxLength(256);
        builder.Property(message => message.CorrelationId).HasMaxLength(128);
        builder.Property(message => message.LastErrorCode).HasMaxLength(128);
    }

    private static void ConfigureInboxReceipt(EntityTypeBuilder<InboxReceipt> builder)
    {
        builder.ToTable("PubSoftInboxReceipts");
        builder.HasKey(receipt => new { receipt.TenantKey, receipt.ConsumerName, receipt.MessageId });
        builder.Property(receipt => receipt.TenantKey).HasMaxLength(32).IsRequired();
        builder.Property(receipt => receipt.ConsumerName).HasMaxLength(128).IsRequired();
        builder.Property(receipt => receipt.ProcessedAt).HasConversion(UtcTicksConverter);
    }
}
