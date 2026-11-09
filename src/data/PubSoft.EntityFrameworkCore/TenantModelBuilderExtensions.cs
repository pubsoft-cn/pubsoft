using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PubSoft.MultiTenancy;

namespace PubSoft.EntityFrameworkCore;

/// <summary>EF mapping for tenant entities.</summary>
public static class TenantModelBuilderExtensions
{
    /// <summary>Maps tenant tables.</summary>
    public static ModelBuilder ConfigurePubSoftTenants(
        this ModelBuilder modelBuilder,
        string tableName = "PubSoftTenants")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var tenantBuilder = modelBuilder.Entity<Tenant>();
        tenantBuilder.ToTable(tableName);
        ConfigureTenant(tenantBuilder);
        return modelBuilder;
    }

    private static void ConfigureTenant(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Code).HasMaxLength(64).IsRequired();
        builder.Property(tenant => tenant.Name).HasMaxLength(100).IsRequired();
        builder.Property(tenant => tenant.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(tenant => tenant.Description).HasMaxLength(200);
        builder.Property(tenant => tenant.IsEnabled).HasDefaultValue(true);
        builder.HasIndex(tenant => tenant.Code).IsUnique();
    }
}