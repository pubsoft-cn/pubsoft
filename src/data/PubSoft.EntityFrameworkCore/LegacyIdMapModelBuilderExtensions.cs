using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Persistence mapping for <see cref="LegacyIdMap"/> migration shim rows.</summary>
public static class LegacyIdMapModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="LegacyIdMap"/> to <c>PubSoftLegacyIdMaps</c>: unique
    /// (EntityType, LegacyId) so a legacy id resolves to exactly one replacement,
    /// plus a (TenantId, EntityType, NewId) index for forward lookups.
    /// </summary>
    public static ModelBuilder ConfigurePubSoftLegacyIdMaps(this ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Entity<LegacyIdMap>(entity =>
        {
            entity.ToTable("PubSoftLegacyIdMaps");
            entity.HasKey(map => map.Id);
            entity.Property(map => map.Id).ValueGeneratedNever();
            entity.Property(map => map.EntityType).HasMaxLength(128).IsRequired();
            entity.HasIndex(map => new { map.EntityType, map.LegacyId }).IsUnique();
            entity.HasIndex(map => new { map.TenantId, map.EntityType, map.NewId });
        });
        return builder;
    }
}
