using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ScopeWriteGuardWaveTests
{
    private sealed class Tenant(long? id) : ITenantContext { public long? TenantId { get; } = id; }
    private sealed class Member(long? id, bool scoped) : IMemberScopeContext { public long? MemberId { get; } = id; public bool IsMemberScoped { get; } = scoped; }

    private sealed class E : Entity<int>, IMultiTenant, IMemberScoped
    {
        public long? TenantId { get; set; }
        public long? MemberId { get; set; }
    }

    private sealed class Ctx(DbContextOptions<Ctx> o, ITenantContext t, IMemberScopeContext? m)
        : PubSoftDbContext(o, t, m)
    {
        public DbSet<E> Es => Set<E>();
    }

    private static Ctx Setup(long? tenant, long? member, bool scoped)
    {
        var c = new Ctx(
            new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options,
            new Tenant(tenant), new Member(member, scoped));
        c.Database.OpenConnection();
        c.Database.EnsureCreated();
        return c;
    }

    [Fact]
    public void AddedEntityWithForeignTenantIdIsRejected()
    {
        var c = Setup(7, null, false);
        c.Es.Add(new E { TenantId = 8 });
        // Host code assigning a foreign TenantId would otherwise silently persist cross-tenant.
        Assert.Throws<InvalidOperationException>(() => c.SaveChanges());
    }

    [Fact]
    public void AddedEntityWithForeignMemberIdIsRejected()
    {
        var c = Setup(7, 3, true);
        c.Es.Add(new E { MemberId = 9 });
        Assert.Throws<InvalidOperationException>(() => c.SaveChanges());
    }

    [Fact]
    public void AddedEntityWithinScopePersists()
    {
        var c = Setup(7, 3, true);
        c.Es.Add(new E { MemberId = 3 });
        c.SaveChanges();
        var e = c.Es.Single();
        Assert.Equal(7, e.TenantId);
        Assert.Equal(3, e.MemberId);
    }

    [Fact]
    public void NullTenantContextAllowsExplicitIds()
    {
        var c = Setup(null, null, false);
        c.Es.Add(new E { TenantId = 8, MemberId = 4 });
        c.SaveChanges();
        // Write path allows explicit ids in global context; the read filter still
        // scopes rows to the current tenant, so assert via the bypass.
        Assert.Single(c.Es.IgnoreQueryFilters());
    }
}
