using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditRetentionWaveTests
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

    private static Ctx Db()
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        return c;
    }

    private static AuditLogRecord Rec(DateTimeOffset started, string path = "/x")
        => new() { StartedAt = started, HttpMethod = "GET", RequestPath = path, Success = true, StatusCode = 200 };

    [Fact]
    public async Task DeleteRemovesOnlyExpired()
    {
        using var ctx = Db();
        var old = Rec(DateTimeOffset.UtcNow.AddDays(-30));
        var fresh = Rec(DateTimeOffset.UtcNow);
        ctx.Set<AuditLogRecord>().AddRange(old, fresh);
        await ctx.SaveChangesAsync();
        var cleanup = new AuditRetentionCleanup<Ctx>(ctx);
        var deleted = await cleanup.DeleteStartedBeforeAsync(TimeSpan.FromDays(7));
        Assert.Equal(1, deleted);
        var remaining = await ctx.Set<AuditLogRecord>().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(fresh.Id, remaining[0].Id);
    }

    [Fact]
    public async Task ArchiveExportsBeforeDelete()
    {
        using var ctx = Db();
        ctx.Set<AuditLogRecord>().Add(Rec(DateTimeOffset.UtcNow.AddDays(-30)));
        await ctx.SaveChangesAsync();
        var exported = new List<AuditLogRecord>();
        var cleanup = new AuditRetentionCleanup<Ctx>(ctx);
        var deleted = await cleanup.ArchiveStartedBeforeAsync(
            TimeSpan.FromDays(7),
            (batch, _) => { exported.AddRange(batch); return Task.CompletedTask; });
        Assert.Equal(1, deleted);
        Assert.Single(exported);
        Assert.Empty(await ctx.Set<AuditLogRecord>().ToListAsync());
    }

    [Fact]
    public async Task DeleteRespectsTenantFilter()
    {
        using var ctx = Db();
        var t1 = Rec(DateTimeOffset.UtcNow.AddDays(-30)); t1.TenantId = 1;
        var host = Rec(DateTimeOffset.UtcNow.AddDays(-30)); // TenantId null = host
        ctx.Set<AuditLogRecord>().AddRange(t1, host);
        await ctx.SaveChangesAsync();
        var cleanup = new AuditRetentionCleanup<Ctx>(ctx);
        var deleted = await cleanup.DeleteStartedBeforeAsync(TimeSpan.FromDays(7), tenantId: 1);
        Assert.Equal(1, deleted); // only tenant-1 row gone
        Assert.Single(await ctx.Set<AuditLogRecord>().Where(r => r.TenantId == null).ToListAsync());
    }
}
