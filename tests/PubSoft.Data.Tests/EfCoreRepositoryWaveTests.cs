using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EfCoreRepositoryWaveTests
{
    private sealed class E : Entity<Guid> { public string Name { get; set; } = ""; }
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        public DbSet<E> Items => Set<E>();
        protected override void OnModelCreating(ModelBuilder b) { base.OnModelCreating(b); b.Entity<E>().HasKey(x => x.Id); }
    }

    private static (Ctx, EfCoreRepository<E, Guid, Ctx>) Repo()
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        return (c, new EfCoreRepository<E, Guid, Ctx>(c));
    }

    [Fact]
    public async Task CrudRoundtrip()
    {
        var (c, r) = Repo();
        var e = await r.InsertAsync(new E { Name = "a" }, autoSave: true);
        Assert.NotEqual(Guid.Empty, e.Id);
        Assert.Equal("a", (await r.GetAsync(e.Id)).Name);
        Assert.Null(await r.FindAsync(Guid.NewGuid()));
        e.Name = "b"; await r.UpdateAsync(e, autoSave: true);
        Assert.Equal("b", (await r.GetAsync(e.Id)).Name);
        await r.DeleteAsync(e.Id, autoSave: true);
        Assert.Empty(await c.Items.ToListAsync());
    }

    [Fact]
    public async Task ManyAndEntityDelete()
    {
        var (c, r) = Repo();
        var es = Enumerable.Range(0, 3).Select(i => new E { Name = $"n{i}" }).ToList();
        await r.InsertManyAsync(es, autoSave: true);
        Assert.Equal(3, await c.Items.CountAsync());
        foreach (var e in es) e.Name += "!";
        await r.UpdateManyAsync(es, autoSave: true);
        Assert.All(await c.Items.ToListAsync(), e => Assert.EndsWith("!", e.Name));
        await r.DeleteAsync(es[0], autoSave: true);
        Assert.Equal(2, await c.Items.CountAsync());
    }

    [Fact]
    public async Task QueryableAndDbSetAndSave()
    {
        var (c, r) = Repo();
        var q = await r.GetQueryableAsync();
        Assert.NotNull(q);
        var ds = await r.GetDbSetAsync();
        Assert.Same(c.Items, ds);
        ds.Add(new E { Name = "x" });
        await r.SaveChangesAsync();
        Assert.Equal(1, await c.Items.CountAsync());
    }

    [Fact]
    public async Task GetAsyncMissingThrows()
    {
        var (_, r) = Repo();
        await Assert.ThrowsAnyAsync<Exception>(() => r.GetAsync(Guid.NewGuid()));
    }
}
