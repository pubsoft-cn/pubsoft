using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>EF mapping for role data scopes.</summary>
public static class RoleDataScopeModelBuilderExtensions
{
    /// <summary>Maps the role-data-scope table.</summary>
    public static ModelBuilder ConfigurePubSoftRoleDataScopes(
        this ModelBuilder modelBuilder,
        string tableName = "pubsoft_role_data_scope")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var builder = modelBuilder.Entity<RoleDataScope>();
        builder.ToTable(tableName);
        builder.HasKey(scope => scope.Id);
        builder.Property(scope => scope.Dimension).IsRequired();
        builder.HasIndex(scope => new { scope.TenantId, scope.RoleId });
        return modelBuilder;
    }
}