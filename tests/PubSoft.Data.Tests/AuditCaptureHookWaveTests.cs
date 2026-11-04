using Microsoft.EntityFrameworkCore;
using PubSoft.Auditing;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditCaptureHookWaveTests
{
    private sealed class E : Entity<long> { public string Name { get; set; } = ""; }
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        public DbSet<E> Items => Set<E>();
        protected override void OnModelCreating(ModelBuilder b) { base.OnModelCreating(b); b.Entity<E>().HasKey(x => x.Id); }
    }

    private static Ctx Db()
    {
        var c = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options);
        c.Database.OpenConnection(); c.Database.EnsureCreated();
        return c;
    }

    [Fact]
    public void HookCapturesAddedEntity()
    {
        using var ctx = Db();
        var collector = new AuditChangeCollector();
        var hook = new AuditChangeCaptureHook(collector);
        ctx.Items.Add(new E { Id = 1, Name = "x" });
        hook.BeforeSaveChanges(ctx);
        Assert.NotEmpty(collector.GetSnapshot()); // tracked-added entity captured pre-save
        Assert.Equal(AuditChangeKind.Created, collector.GetSnapshot()[0].Kind);
    }

    [Fact]
    public void HookIgnoresUntrackedContext()
    {
        using var ctx = Db();
        var collector = new AuditChangeCollector();
        var hook = new AuditChangeCaptureHook(collector);
        hook.BeforeSaveChanges(ctx); // nothing tracked
        Assert.Empty(collector.GetSnapshot());
    }
}
