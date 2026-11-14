using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PubSoft.EntityFrameworkCore.Auditing;


/// <summary>EF mapping for audit-log and entity-change records.</summary>
public static class AuditPersistenceModelBuilderExtensions
{
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter = new(
        instant => instant.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    /// <summary>Maps the audit tables; Snowflake ids are application-assigned, timestamps stored as UTC ticks.</summary>
    public static ModelBuilder ConfigurePubSoftAuditPersistence(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AuditLogRecord>(ConfigureAuditLog);
        modelBuilder.Entity<AuditEntityChangeRecord>(ConfigureEntityChange);
        return modelBuilder;
    }

    private static void ConfigureAuditLog(EntityTypeBuilder<AuditLogRecord> builder)
    {
        builder.ToTable("PubSoftAuditLogs");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.HasIndex(record => new { record.TenantId, record.StartedAt });
        builder.HasIndex(record => new { record.TenantId, record.Category, record.Action });
        builder.HasIndex(record => new { record.TenantId, record.SubjectId, record.StartedAt });
        builder.Property(record => record.StartedAt).HasConversion(UtcTicksConverter);
        builder.Property(record => record.HttpMethod).HasMaxLength(16);
        builder.Property(record => record.RequestPath).HasMaxLength(2_048);
        builder.Property(record => record.ClientIp).HasMaxLength(128);
        builder.Property(record => record.UserAgent).HasMaxLength(2_048);
        builder.Property(record => record.SubjectId).HasMaxLength(256);
        builder.Property(record => record.SubjectName).HasMaxLength(512);
        builder.Property(record => record.FailureCode).HasMaxLength(128);
        builder.Property(record => record.Category).HasMaxLength(128);
        builder.Property(record => record.Action).HasMaxLength(128);
        builder.Property(record => record.TargetType).HasMaxLength(256);
        builder.Property(record => record.TargetId).HasMaxLength(256);
        builder.Property(record => record.TargetName).HasMaxLength(512);
        builder.Property(record => record.Detail).HasColumnType("text"); // utf8mb4 varchar cap is 16383 chars - text removes the ceiling; upstream truncation already bounds content
        builder.HasMany(record => record.EntityChanges)
            .WithOne(change => change.AuditLog)
            .HasForeignKey(change => change.AuditLogId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureEntityChange(EntityTypeBuilder<AuditEntityChangeRecord> builder)
    {
        builder.ToTable("PubSoftAuditEntityChanges");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.HasIndex(change => new { change.TenantId, change.AuditLogId });
        builder.Property(change => change.ChangedAt).HasConversion(UtcTicksConverter);
        builder.Property(change => change.EntityType).HasMaxLength(256).IsRequired();
        builder.Property(change => change.EntityId).HasMaxLength(512).IsRequired();
        builder.Property(change => change.PropertiesJson).IsRequired();
    }
}
