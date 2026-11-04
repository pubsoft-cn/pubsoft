using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class PermissionGrantWaveTests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.ConfigurePubSoftPermissionGrants();
        }
    }

    private static Ctx Db()
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        return c;
    }

    [Fact]
    public async Task ReplaceRoleGrantsRoundtrip()
    {
        using var ctx = Db();
        ctx.Set<PermissionRole>().Add(new PermissionRole(1, "admin"));
        await ctx.SaveChangesAsync();
        var mgr = new EfCorePermissionGrantManager<Ctx>(ctx);
        var grants = new[]
        {
            new PermissionGrantRecord(10, 1, "orders", flags: 1),
            new PermissionGrantRecord(11, 1, "billing", flags: 2),
        };
        await mgr.ReplaceRoleGrantsAsync(1, null, grants);
        var stored = await ctx.Set<PermissionGrantRecord>().Where(g => g.RoleId == 1).ToListAsync();
        Assert.Equal(2, stored.Count);
        await mgr.ReplaceRoleGrantsAsync(1, null, [grants[0]]); // replace shrinks to one
        Assert.Single(await ctx.Set<PermissionGrantRecord>().Where(g => g.RoleId == 1).ToListAsync());
    }

    [Fact]
    public async Task ReplaceGrantsRejectsBadInput()
    {
        using var ctx = Db();
        var mgr = new EfCorePermissionGrantManager<Ctx>(ctx);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => mgr.ReplaceRoleGrantsAsync(0, null, [new PermissionGrantRecord(1, 1, "m", flags: 1)]));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => mgr.ReplaceRoleGrantsAsync(99, null, [new PermissionGrantRecord(1, 99, "m", flags: 1)]));
    }

    [Fact]
    public async Task AssignSubjectRolesAndProviderReads()
    {
        using var ctx = Db();
        ctx.Set<PermissionRole>().AddRange(new PermissionRole(1, "r1"), new PermissionRole(2, "r2"));
        await ctx.SaveChangesAsync();
        var mgr = new EfCorePermissionGrantManager<Ctx>(ctx);
        await mgr.AssignSubjectRolesAsync(42, null, [1L, 2L]);
        var assignments = await ctx.Set<SubjectPermissionRole>().Where(s => s.SubjectId == 42).ToListAsync();
        Assert.Equal(2, assignments.Count);
        var prov = new EfCorePermissionGrantProvider<Ctx>(ctx);
        var grants = await prov.GetGrantsAsync(42, null);
        Assert.NotNull(grants); // provider reads through the assignment chain
    }
}
