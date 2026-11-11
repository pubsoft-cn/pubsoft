using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditQueryServiceWaveTests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
    private sealed class ScopedTenant : ITenantContext { public long? TenantId => 7; }
    private sealed class ScopedCtx(DbContextOptions<ScopedCtx> o) : PubSoftDbContext(o, new ScopedTenant())
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.ConfigurePubSoftAuditPersistence();
        }
    }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.ConfigurePubSoftAuditPersistence();
        }
    }

    private static (Ctx, AuditLogQueryService<Ctx>) Build(params AuditLogRecord[] rows)
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        c.Set<AuditLogRecord>().AddRange(rows);
        c.SaveChanges();
        return (c, new AuditLogQueryService<Ctx>(c));
    }

    private static AuditLogRecord Rec(string path, bool ok = true, string? cat = null, string? act = null, long? tenant = null)
        => new()
        {
            Id = PubSoft.Testing.TestIds.Next(),
            HttpMethod = "GET",
            RequestPath = path,
            Success = ok,
            StatusCode = ok ? 200 : 500,
            Category = cat,
            Action = act,
            TenantId = tenant,
            StartedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task GetListFiltersByCategoryAndSuccess()
    {
        var (_, s) = Build(
            Rec("/a", ok: true, cat: "orders", act: "created"),
            Rec("/b", ok: false, cat: "orders", act: "failed"),
            Rec("/c", ok: true, cat: "billing"));
        var r = await s.GetListAsync(category: "orders", includeAllTenants: true);
        Assert.Equal(2, r.TotalCount);
        var failed = await s.GetListAsync(success: false, includeAllTenants: true);
        Assert.Single(failed.Items);
        Assert.Equal("/b", failed.Items[0].RequestPath);
    }

    [Fact]
    public async Task PagingClampsAndKeywordMatches()
    {
        var (_, s) = Build(Rec("/api/orders/1"), Rec("/api/users/2"), Rec("/api/orders/3"));
        var r = await s.GetListAsync(take: 999, includeAllTenants: true); // clamp to 100
        Assert.Equal(3, r.Items.Count);
        var kw = await s.GetListAsync(keyword: "orders", includeAllTenants: true);
        Assert.Equal(2, kw.TotalCount);
    }

    // R2: missing tenant context must NOT auto-elevate to cross-tenant read.
    [Fact]
    public async Task GetListWithoutTenantContextReturnsEmptyUnlessOptedIn()
    {
        var (_, s) = Build(Rec("/x", tenant: 5), Rec("/y", tenant: 9));

        var scopedOut = await s.GetListAsync();
        Assert.Empty(scopedOut.Items);
        Assert.Equal(0, scopedOut.TotalCount);

        var all = await s.GetListAsync(includeAllTenants: true);
        Assert.Equal(2, all.TotalCount);
    }

    // R1: under a scoped context, deleting a null-TenantId (host-shared) entity must
    // be rejected - the attach+delete path bypasses query filters.
    [Fact]
    public void ScopedContextCannotDeleteNullTenantRecord()
    {
        var c = new ScopedCtx(new DbContextOptionsBuilder<ScopedCtx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        var orphan = Rec("/host", tenant: null);
        orphan.Id = PubSoft.Testing.TestIds.Next();
        c.Set<AuditLogRecord>().Attach(orphan);
        c.Remove(orphan);
        var ex = Assert.Throws<InvalidOperationException>(() => c.SaveChanges());
        Assert.Contains("outside the current tenant scope", ex.Message);
    }
}

