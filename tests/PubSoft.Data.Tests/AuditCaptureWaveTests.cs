using Microsoft.EntityFrameworkCore;
using PubSoft.Auditing;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditCaptureWaveTests
{
    private sealed class ProbeEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        [PubSoft.Auditing.AuditSensitive] public string? BankAccount { get; set; }
    }

    private sealed class Ctx(DbContextOptions<Ctx> o) : DbContext(o)
    {
        public DbSet<ProbeEntity> Probes => Set<ProbeEntity>();
    }

    private sealed class Collector : IAuditChangeCollector
    {
        public List<AuditEntityChange> Changes { get; } = [];
        public IReadOnlyList<AuditEntityChange> GetSnapshot() => Changes;
        public void Add(AuditEntityChange c) => Changes.Add(c);
        public void AddRange(IReadOnlyCollection<AuditEntityChange> cs) => Changes.AddRange(cs);
        public void Clear() => Changes.Clear();
    }

    private static (Ctx ctx, Collector col, AuditChangeCaptureHook hook) Setup()
    {
        var opts = new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options;
        var ctx = new Ctx(opts);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        var col = new Collector();
        return (ctx, col, new AuditChangeCaptureHook(col));
    }

    [Fact]
    public void AddedEntityCapturesCreatedKind()
    {
        var (ctx, col, hook) = Setup();
        ctx.Probes.Add(new ProbeEntity { Name = "x" });
        hook.BeforeSaveChanges(ctx);
        Assert.Single(col.Changes);
        Assert.Equal("ProbeEntity", col.Changes[0].EntityType);
        Assert.Equal(AuditChangeKind.Created, col.Changes[0].Kind);
    }

    [Fact]
    public void UnchangedEntitiesSkipped()
    {
        var (ctx, col, hook) = Setup();
        var e = new ProbeEntity { Name = "x" };
        ctx.Probes.Add(e);
        ctx.SaveChanges();
        col.Clear();
        hook.BeforeSaveChanges(ctx); // nothing dirty
        Assert.Empty(col.Changes);
    }

    [Fact]
    public void AuditSensitiveAttributeRedactsValue()
    {
        var (ctx, col, _) = Setup();
        var hook = new AuditChangeCaptureHook(col,
            new AuditChangeCollectorOptions { CaptureValues = true });
        ctx.Probes.Add(new ProbeEntity { Name = "x", BankAccount = "top-secret" });
        hook.BeforeSaveChanges(ctx);
        var change = col.Changes[0].Properties.First(p => p.Name == "BankAccount");
        Assert.Contains("REDACTED", change.NewValue?.GetString() ?? "");
        Assert.DoesNotContain("top-secret", change.NewValue?.GetString() ?? "");
    }

    [Fact]
    public void IgnoredEntityTypesFiltered()
    {
        var (ctx, col, _) = Setup();
        var hook = new AuditChangeCaptureHook(col,
            new AuditChangeCollectorOptions { IgnoredEntityTypes = ["ProbeEntity"] });
        ctx.Probes.Add(new ProbeEntity { Name = "x" });
        hook.BeforeSaveChanges(ctx);
        Assert.Empty(col.Changes);
    }
}
