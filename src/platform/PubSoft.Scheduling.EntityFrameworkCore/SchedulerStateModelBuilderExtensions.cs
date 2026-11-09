using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PubSoft.Scheduling.EntityFrameworkCore;

/// <summary>EF mapping for <see cref="SchedulerStateRecord"/>.</summary>
public static class SchedulerStateModelBuilderExtensions
{
    /// <summary>Maps the scheduler-state table with the (TenantScope, KeyHash) uniqueness invariant.</summary>
    public static ModelBuilder ConfigurePubSoftSchedulerState(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var entityBuilder = modelBuilder.Entity<SchedulerStateRecord>();
        entityBuilder.ToTable("PubSoftSchedulerState");
        ConfigureRecord(entityBuilder);
        return modelBuilder;
    }

    private static void ConfigureRecord(EntityTypeBuilder<SchedulerStateRecord> builder)
    {
        builder.HasKey(record => record.Id);
        builder.Property(record => record.TenantScope).HasMaxLength(32).IsRequired();
        builder.Property(record => record.Key).IsRequired();
        builder.Property(record => record.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ValueJson).IsRequired();
        builder.HasIndex(record => new { record.TenantScope, record.KeyHash }).IsUnique();
        builder.HasIndex(record => record.ExpiresAt);
    }
}