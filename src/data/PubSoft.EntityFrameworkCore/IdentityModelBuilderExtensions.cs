using Microsoft.EntityFrameworkCore;
using PubSoft.Identity;

namespace PubSoft.EntityFrameworkCore;

public static class IdentityModelBuilderExtensions
{
    public static ModelBuilder ConfigurePubSoftIdentityUsers(
        this ModelBuilder modelBuilder,
        string tableName = "pubsoft_identity_user")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var users = modelBuilder.Entity<IdentityUser>();
        users.ToTable(tableName);
        users.HasKey(user => user.Id);
        users.Property(user => user.UserName).HasMaxLength(256).IsRequired();
        users.Property(user => user.NormalizedUserName).HasMaxLength(256).IsRequired();
        users.Property(user => user.Name).HasMaxLength(256).IsRequired();
        users.Property(user => user.PhoneNumber).HasMaxLength(32);
        users.Property(user => user.PasswordHash).HasMaxLength(512);
        users.Property(user => user.SecurityStamp).HasMaxLength(64).IsRequired();
        users.HasIndex(user => new { user.TenantId, user.NormalizedUserName }).IsUnique();
        return modelBuilder;
    }
}