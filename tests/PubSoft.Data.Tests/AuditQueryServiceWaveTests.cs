using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditQueryServiceWaveTests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
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
        => new() { HttpMethod = "GET", RequestPath = path, Success = ok, StatusCode = ok ? 200 : 500,
                   Category = cat, Action = act, TenantId = tenant, StartedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task GetListFiltersByCategoryAndSuccess()
    {
        var (_, s) = Build(
            Rec("/a", ok: true, cat: "orders", act: "created"),
            Rec("/b", ok: false, cat: "orders", act: "failed"),
            Rec("/c", ok: true, cat: "billing"));
        var r = await s.GetListAsync(category: "orders");
        Assert.Equal(2, r.TotalCount);
        var failed = await s.GetListAsync(success: false);
        Assert.Single(failed.Items);
        Assert.Equal("/b", failed.Items[0].RequestPath);
    }

    [Fact]
    public async Task PagingClampsAndKeywordMatches()
    {
        var (_, s) = Build(Rec("/api/orders/1"), Rec("/api/users/2"), Rec("/api/orders/3"));
        var r = await s.GetListAsync(take: 999); // clamp to 100
        Assert.Equal(3, r.Items.Count);
        var kw = await s.GetListAsync(keyword: "orders");
        Assert.Equal(2, kw.TotalCount);
    }
}
