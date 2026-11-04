using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DbContextWaveTests
{
    private sealed class Tenant(long? id) : ITenantContext { public long? TenantId { get; } = id; }
    private sealed class Member(long? id, bool scoped) : IMemberScopeContext { public long? MemberId { get; } = id; public bool IsMemberScoped { get; } = scoped; }
    private sealed class Hook : ISaveChangesHook { public int Calls; public void BeforeSaveChanges(DbContext c) => Calls++; }

    private sealed class E : Entity<int>, IMultiTenant { public long? TenantId { get; set; } }
    private sealed class Ctx(DbContextOptions<Ctx> o, ITenantContext t, IMemberScopeContext? m, ISaveChangesHook? h) : PubSoftDbContext(o, t, m, null, h)
    {
        public DbSet<E> Es => Set<E>();
    }

    private static (Ctx, Hook) Setup(long? tenant = 7, long? member = 3, bool scoped = true)
    {
        var opts = new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options;
        var h = new Hook();
        var c = new Ctx(opts, new Tenant(tenant), new Member(member, scoped), h);
        c.Database.OpenConnection();
        c.Database.EnsureCreated();
        return (c, h);
    }

    [Fact]
    public void CurrentContextProjects()
    {
        var (ctx, _) = Setup();
        Assert.Equal(7, ctx.CurrentTenantId);
        Assert.Equal(3, ctx.CurrentMemberId);
        Assert.True(ctx.IsMemberScoped);
    }

    [Fact]
    public void AddedEntityAutoFillsTenantId()
    {
        var (ctx, _) = Setup();
        ctx.Es.Add(new E());
        ctx.SaveChanges();
        var e = ctx.Es.First();
        Assert.Equal(7, e.TenantId);
    }

    [Fact]
    public void SaveChangesInvokesHook()
    {
        var (ctx, hook) = Setup();
        ctx.Es.Add(new E { TenantId = 7 });
        ctx.SaveChanges();
        Assert.True(hook.Calls > 0);
    }

    [Fact]
    public void FilterNameConstants()
    {
        Assert.Equal("PubSoft.Tenant", PubSoftDbContext.TenantFilter);
        Assert.Equal("PubSoft.Member", PubSoftDbContext.MemberFilter);
        Assert.Equal("PubSoft.SoftDelete", PubSoftDbContext.SoftDeleteFilter);
    }
}
