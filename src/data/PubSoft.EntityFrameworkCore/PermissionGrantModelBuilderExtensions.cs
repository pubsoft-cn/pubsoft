using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>EF mapping for permission grants.</summary>
public static class PermissionGrantModelBuilderExtensions
{
    /// <summary>Maps permission-grant tables.</summary>
    public static ModelBuilder ConfigurePubSoftPermissionGrants(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var roles = modelBuilder.Entity<PermissionRole>();
        roles.ToTable("pubsoft_permission_role");
        roles.HasKey(role => role.Id);
        roles.Property(role => role.ClientCode).HasMaxLength(64).HasDefaultValue(string.Empty).IsRequired();
        roles.Property(role => role.Name).HasMaxLength(128).IsRequired();
        roles.HasIndex(role => new { role.TenantId, role.ClientCode, role.Name }).IsUnique();

        var assignments = modelBuilder.Entity<SubjectPermissionRole>();
        assignments.ToTable("pubsoft_subject_permission_role");
        assignments.HasKey(assignment => assignment.Id);
        assignments.Property(assignment => assignment.ClientCode).HasMaxLength(64).HasDefaultValue(string.Empty).IsRequired();
        assignments.HasIndex(assignment => new { assignment.TenantId, assignment.ClientCode, assignment.SubjectId });
        assignments.HasIndex(assignment => new { assignment.TenantId, assignment.ClientCode, assignment.RoleId, assignment.SubjectId }).IsUnique();

        var grants = modelBuilder.Entity<PermissionGrantRecord>();
        grants.ToTable("pubsoft_permission_grant", table =>
        {
            table.HasCheckConstraint(
                "CK_pubsoft_permission_grant_target",
                "RoleId IS NOT NULL");
            table.HasCheckConstraint(
                "CK_pubsoft_permission_grant_payload",
                "Flags <> 0 OR ExtensionFlags <> 0 OR (CustomPermissionName IS NOT NULL AND CustomPermissionName <> '')");
        });
        grants.HasKey(grant => grant.Id);
        grants.Property(grant => grant.ModuleKey).HasMaxLength(128).IsRequired();
        grants.Property(grant => grant.CustomPermissionName).HasMaxLength(256);
        grants.HasIndex(grant => new { grant.TenantId, grant.RoleId, grant.ModuleKey });

        var modules = modelBuilder.Entity<PermissionModule>();
        modules.ToTable("pubsoft_permission_module");
        modules.HasKey(module => module.Id);
        modules.Property(module => module.ClientCode).HasMaxLength(64).IsRequired();
        modules.Property(module => module.ModuleKey).HasMaxLength(128).IsRequired();
        modules.Property(module => module.DisplayName).HasMaxLength(200).IsRequired();
        modules.Property(module => module.Description).HasMaxLength(1000);
        modules.Property(module => module.CustomPermissionsJson).HasMaxLength(4000);
        modules.HasIndex(module => new { module.TenantId, module.ClientCode, module.ModuleKey }).IsUnique();
        return modelBuilder;
    }
}