using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

public static class RoleDataScopeModelBuilderExtensions
{
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