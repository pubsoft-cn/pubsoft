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
        users.Property(user => user.AccessFailedCount);
        users.Property(user => user.LockoutEnd);
        users.Property(user => user.ExtJson);
        users.HasIndex(user => new { user.TenantId, user.NormalizedUserName }).IsUnique();
        return modelBuilder;
    }

    /// <summary>Maps <see cref="IdentityUserLogin"/> -- external provider bindings, unique on (provider, providerKey).</summary>
    public static ModelBuilder ConfigurePubSoftIdentityUserLogins(
        this ModelBuilder modelBuilder,
        string tableName = "pubsoft_identity_user_login")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var logins = modelBuilder.Entity<IdentityUserLogin>();
        logins.ToTable(tableName);
        logins.HasKey(login => login.Id);
        logins.Property(login => login.UserId).IsRequired();
        logins.Property(login => login.LoginProvider).HasMaxLength(64).IsRequired();
        logins.Property(login => login.ProviderKey).HasMaxLength(256).IsRequired();
        logins.Property(login => login.ProviderDisplayName).HasMaxLength(128);
        logins.Property(login => login.ExtJson);
        logins.HasIndex(login => new { login.TenantId, login.LoginProvider, login.ProviderKey }).IsUnique();
        logins.HasIndex(login => login.UserId);
        return modelBuilder;
    }
}