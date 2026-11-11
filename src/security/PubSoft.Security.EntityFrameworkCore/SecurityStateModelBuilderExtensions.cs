using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PubSoft.Security.EntityFrameworkCore;

/// <summary>EF mapping for the PubSoft.Security persistence tables.</summary>
public static class SecurityStateModelBuilderExtensions
{
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter =
        new(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    /// <summary>
    /// Maps the refresh-token family state, family revocation, and token denylist tables.
    /// Call from the host DbContext's OnModelCreating; the host owns the schema and migrations.
    /// Timestamps persist as UTC ticks so expiry comparisons translate on every provider.
    /// </summary>
    public static ModelBuilder ConfigurePubSoftSecurityState(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var stateBuilder = modelBuilder.Entity<RefreshTokenFamilyStateRecord>();
        stateBuilder.ToTable("PubSoftRefreshTokenFamilyStates");
        stateBuilder.HasKey(record => record.Id);
        stateBuilder.Property(record => record.Id).ValueGeneratedNever();
        stateBuilder.Property(record => record.TokenHash).HasMaxLength(64).IsRequired();
        stateBuilder.Property(record => record.FamilyId).HasMaxLength(64).IsRequired();
        stateBuilder.Property(record => record.Subject).HasMaxLength(256).IsRequired();
        stateBuilder.Property(record => record.IssuedAt).HasConversion(UtcTicksConverter);
        stateBuilder.Property(record => record.TokenExpiresAt).HasConversion(UtcTicksConverter);
        stateBuilder.Property(record => record.FamilyExpiresAt).HasConversion(UtcTicksConverter);
        stateBuilder.HasIndex(record => record.TokenHash).IsUnique();
        stateBuilder.HasIndex(record => record.FamilyId);
        stateBuilder.HasIndex(record => record.Subject);
        stateBuilder.HasIndex(record => record.TokenExpiresAt);

        var revocationBuilder = modelBuilder.Entity<RefreshTokenFamilyRevocationRecord>();
        revocationBuilder.ToTable("PubSoftRefreshTokenFamilyRevocations");
        revocationBuilder.HasKey(record => record.Id);
        revocationBuilder.Property(record => record.Id).ValueGeneratedNever();
        revocationBuilder.Property(record => record.FamilyId).HasMaxLength(64).IsRequired();
        revocationBuilder.Property(record => record.ExpiresAt).HasConversion(UtcTicksConverter);
        revocationBuilder.HasIndex(record => record.FamilyId).IsUnique();
        revocationBuilder.HasIndex(record => record.ExpiresAt);

        var denyBuilder = modelBuilder.Entity<TokenDenylistRecord>();
        denyBuilder.ToTable("PubSoftTokenDenylist");
        denyBuilder.HasKey(record => record.Id);
        denyBuilder.Property(record => record.Id).ValueGeneratedNever();
        denyBuilder.Property(record => record.TokenId).HasMaxLength(128).IsRequired();
        denyBuilder.Property(record => record.Subject).HasMaxLength(256);
        denyBuilder.Property(record => record.Reason).HasMaxLength(256);
        denyBuilder.Property(record => record.DeniedAt).HasConversion(UtcTicksConverter);
        denyBuilder.Property(record => record.ExpiresAt).HasConversion(UtcTicksConverter);
        denyBuilder.HasIndex(record => record.TokenId).IsUnique();
        denyBuilder.HasIndex(record => record.ExpiresAt);

        return modelBuilder;
    }
}
