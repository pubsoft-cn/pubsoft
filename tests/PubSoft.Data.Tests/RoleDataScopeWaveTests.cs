using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RoleDataScopeWaveTests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => 1; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.ConfigurePubSoftPermissionGrants();
            b.ConfigurePubSoftRoleDataScopes();
        }
    }

    private static Ctx Db()
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        return c;
    }

    [Fact]
    public async Task ScopesResolveThroughAssignmentChain()
    {
        using var ctx = Db();
        ctx.Set<PermissionRole>().Add(new PermissionRole(1, "dept-admin", tenantId: 1));
        ctx.Set<SubjectPermissionRole>().Add(new SubjectPermissionRole(1, 42, 1, tenantId: 1));
        ctx.Set<RoleDataScope>().AddRange(
            new RoleDataScope(1, 1, "dept", 100, tenantId: 1),
            new RoleDataScope(2, 1, "site", 200, tenantId: 1));
        await ctx.SaveChangesAsync();
        var prov = new EfCoreRoleDataScopeProvider<Ctx>(ctx);
        var dims = await prov.GetRoleScopesAsync(42, 1);
        Assert.Equal(2, dims.Count);
    }

    [Fact]
    public async Task TenantMismatchAndInvalidIdsReturnEmpty()
    {
        using var ctx = Db();
        var prov = new EfCoreRoleDataScopeProvider<Ctx>(ctx);
        Assert.Empty(await prov.GetRoleScopesAsync(0, 1));      // bad subjectId
        Assert.Empty(await prov.GetRoleScopesAsync(1, 2));      // tenant mismatch (ctx tenant=1)
        Assert.Empty(await prov.GetRoleScopesAsync(1, null));   // null tenant
    }
}
