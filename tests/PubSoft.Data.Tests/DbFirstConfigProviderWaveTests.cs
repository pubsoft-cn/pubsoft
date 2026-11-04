using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DbFirstConfigProviderWaveTests
{
    private sealed class E : Entity<long> { public string K { get; set; } = ""; public string V { get; set; } = ""; public bool Enabled { get; set; } = true; }
    private sealed class Info { public string K = ""; public string V = ""; }
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        public DbSet<E> Items => Set<E>();
        protected override void OnModelCreating(ModelBuilder b) { base.OnModelCreating(b); b.Entity<E>().HasKey(x => x.Id); }
    }

    private sealed class Prov(IServiceProvider sp, IConfiguration cfg, IMemoryCache cache)
        : DbFirstConfigProvider<E, Info>(sp, cfg, cache)
    {
        protected override string CachePrefix => "cfg:";
        protected override string FallbackSectionName => "Fallback";
        protected override string ConfigKey(Info info) => info.K;
        protected override Expression<Func<E, bool>> DbPredicate(string key) => e => e.K == key && e.Enabled;
        protected override Info ToInfo(E e) => new() { K = e.K, V = e.V };
    }

    private static (Ctx, Prov) Build()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<Ctx>().UseSqlite(conn).Options;
        var c = new Ctx(opts); c.Database.EnsureCreated();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        sc.AddSingleton(_ => c); // singleton — scope disposal must not kill the shared ctx
        sc.AddSingleton<IRepository<E, long>>(sp => new EfCoreRepository<E, long, Ctx>(sp.GetRequiredService<Ctx>()));
        return (c, new Prov(sc.BuildServiceProvider(), cfg, cache));
    }

    [Fact]
    public async Task DbHitThenCached()
    {
        var (c, p) = Build();
        c.Items.Add(new E { K = "k1", V = "db" }); await c.SaveChangesAsync();
        Assert.Equal("db", (await p.GetConfigAsync("k1"))!.V);
        c.Items.First(x => x.K == "k1").V = "changed"; await c.SaveChangesAsync();
        Assert.Equal("db", (await p.GetConfigAsync("k1"))!.V); // cache wins
    }

    [Fact]
    public async Task InvalidateCacheRequeries()
    {
        var (c, p) = Build();
        c.Items.Add(new E { K = "k1", V = "v1" }); await c.SaveChangesAsync();
        await p.GetConfigAsync("k1");
        c.Items.First(x => x.K == "k1").V = "v2"; await c.SaveChangesAsync();
        p.InvalidateCache("k1");
        Assert.Equal("v2", (await p.GetConfigAsync("k1"))!.V);
    }

    [Fact]
    public async Task GetAllFromDb()
    {
        var (c, p) = Build();
        c.Items.AddRange(new E { K = "a", V = "1" }, new E { K = "b", V = "2" });
        await c.SaveChangesAsync();
        var all = await p.GetAllConfigsAsync();
        Assert.Equal(2, all.Count);
    }
}
