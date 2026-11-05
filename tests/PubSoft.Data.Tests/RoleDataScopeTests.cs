using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RoleDataScopeTests
{
    [Fact]
    public async Task RoleDataScopesAreAssignedAndFilteredByTenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoleDataScopeDbContext>().UseSqlite(connection).Options;

        await using (var tenantContext = new RoleDataScopeDbContext(options, new TestTenantContext(73)))
        {
            await tenantContext.Database.EnsureCreatedAsync();
            Assert.Equal("pubsoft_role_data_scope", tenantContext.Model.FindEntityType(typeof(RoleDataScope))!.GetTableName());
            Assert.Contains(
                tenantContext.Model.FindEntityType(typeof(RoleDataScope))!.GetIndexes(),
                index => index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(RoleDataScope.TenantId), nameof(RoleDataScope.RoleId)]));
            tenantContext.RoleDataScopes.Add(new RoleDataScope(1, 5, "store", 91, isAll: true));
            await tenantContext.SaveChangesAsync();

            var stored = await tenantContext.RoleDataScopes.SingleAsync();
            Assert.Equal(73, stored.TenantId);
            Assert.Equal(5, stored.RoleId);
            Assert.Equal("store", stored.Dimension);
            Assert.Equal(91, stored.ScopeId);
            Assert.True(stored.IsAll);
        }

        await using (var otherTenant = new RoleDataScopeDbContext(options, new TestTenantContext(41)))
        {
            Assert.Empty(await otherTenant.RoleDataScopes.ToListAsync());
            otherTenant.RoleDataScopes.Add(new RoleDataScope(2, 5, "store", 92));
            await otherTenant.SaveChangesAsync();
        }

        await using var verify = new RoleDataScopeDbContext(options, new TestTenantContext(73));
        Assert.Equal(1, await verify.RoleDataScopes.CountAsync());
        Assert.Equal(2, await verify.RoleDataScopes.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task EfProviderLoadsOnlyAssignedEnabledSameTenantRoleScopesAndUnionsDimensions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoleDataScopeDbContext>().UseSqlite(connection).Options;

        await using (var seed = new RoleDataScopeDbContext(options, new TestTenantContext(73)))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Roles.AddRange(
                new PermissionRole(10, "reader"),
                new PermissionRole(11, "store-admin"),
                new PermissionRole(12, "disabled") { IsEnabled = false });
            seed.SubjectRoles.AddRange(
                new SubjectPermissionRole(20, 90, 10),
                new SubjectPermissionRole(21, 90, 11),
                new SubjectPermissionRole(22, 90, 12));
            seed.RoleDataScopes.AddRange(
                new RoleDataScope(30, 10, "store", 7),
                new RoleDataScope(31, 11, "STORE", 8),
                new RoleDataScope(32, 11, "device", 0, isAll: true),
                new RoleDataScope(33, 12, "store", 99));
            await seed.SaveChangesAsync();
        }

        // Foreign-tenant fixture rows are written under that tenant's own scope —
        // the write guard now rejects explicit foreign TenantIds on Added entities.
        await using (var foreign = new RoleDataScopeDbContext(options, new TestTenantContext(41)))
        {
            foreign.Roles.Add(new PermissionRole(13, "foreign-tenant", tenantId: 41));
            foreign.SubjectRoles.Add(new SubjectPermissionRole(23, 90, 13, tenantId: 41));
            foreign.RoleDataScopes.Add(new RoleDataScope(34, 13, "store", 100, tenantId: 41));
            await foreign.SaveChangesAsync();
        }

        await using var context = new RoleDataScopeDbContext(options, new TestTenantContext(73));
        var provider = new EfCoreRoleDataScopeProvider<RoleDataScopeDbContext>(context);
        var scopes = await provider.GetRoleScopesAsync(90, 73);

        Assert.Equal(2, scopes.Count);
        var store = Assert.Single(scopes, scope => scope.Name == "store");
        Assert.False(store.IsAll);
        Assert.Equal([7L, 8L], store.AllowedIds.Order());
        var device = Assert.Single(scopes, scope => scope.Name == "device");
        Assert.True(device.IsAll);
        Assert.Empty(device.AllowedIds);
        var dimensions = new DataScopeDimensionRegistry();
        dimensions.Add("store");
        dimensions.Add("device");
        var evaluator = new DataScopeAccessEvaluator(dimensions);
        Assert.True(evaluator.CanAccess("store", 7, scopes));
        Assert.True(evaluator.CanAccess("store", 8, scopes));
        Assert.False(evaluator.CanAccess("store", 9, scopes));
        Assert.True(evaluator.CanAccess("device", 9, scopes));
        Assert.Empty(await provider.GetRoleScopesAsync(91, 73));
        Assert.Empty(await provider.GetRoleScopesAsync(90, 41));
        Assert.Empty(await provider.GetRoleScopesAsync(0, 73));
    }

    private sealed class RoleDataScopeDbContext(
        DbContextOptions<RoleDataScopeDbContext> options,
        ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        public DbSet<RoleDataScope> RoleDataScopes => Set<RoleDataScope>();
        public DbSet<PermissionRole> Roles => Set<PermissionRole>();
        public DbSet<SubjectPermissionRole> SubjectRoles => Set<SubjectPermissionRole>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftRoleDataScopes();
        }
    }

    private sealed class TestTenantContext(long? tenantId) : ITenantContext
    {
        public long? TenantId { get; } = tenantId;
    }
}