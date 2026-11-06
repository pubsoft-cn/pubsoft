using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using PubSoft.Testing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditLogWriterTests
{
    [Fact]
    public async Task WriterProducesNonHttpSnapshotWithBusinessFields()
    {
        var sink = new RecordingSink();
        var time = new TestTimeProvider(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero));
        var writer = new AuditLogWriter(sink, time);

        await writer.WriteAsync(new AuditLogEntry(
            new AuditBusinessContext("Security", "PasswordReset", "User", "42", "alice", "via admin"),
            Actor: new AuditActorContext("7", "admin", true),
            Success: true,
            Duration: TimeSpan.FromMilliseconds(150)));

        var snapshot = Assert.Single(sink.Snapshots);
        Assert.Equal("Security", snapshot.Business.Category);
        Assert.Equal("PasswordReset", snapshot.Business.Action);
        Assert.Equal("42", snapshot.Business.TargetId);
        Assert.Equal("via admin", snapshot.Business.Detail);
        Assert.Equal("7", snapshot.Request.Actor?.SubjectId);
        Assert.Null(snapshot.Request.HttpMethod);
        Assert.Equal(0, snapshot.StatusCode);
        Assert.Equal(TimeSpan.FromMilliseconds(150), snapshot.Duration);
        Assert.True(snapshot.Success);
    }

    [Fact]
    public async Task MetricsAreAppendedToDetailDeterministically()
    {
        var sink = new RecordingSink();
        var writer = new AuditLogWriter(sink);

        await writer.WriteAsync(new AuditLogEntry(
            new AuditBusinessContext(Detail: "login"),
            Metrics: new Dictionary<string, string> { ["retries"] = "2", ["ip"] = "10.0.0.1" }));

        Assert.Equal("login | ip=10.0.0.1 retries=2", sink.Snapshots.Single().Business.Detail);
    }

    [Fact]
    public async Task FailureEntriesGetDefaultFailureCode()
    {
        var sink = new RecordingSink();
        var writer = new AuditLogWriter(sink);

        await writer.WriteAsync(new AuditLogEntry(
            new AuditBusinessContext(Category: "Login"),
            Success: false));

        var snapshot = sink.Snapshots.Single();
        Assert.False(snapshot.Success);
        Assert.Equal("OperationFailed", snapshot.FailureCode);
    }

    [Fact]
    public async Task RegisteredWriterPersistsAuditLogRecord()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ITenantContext>(new FixedTenantContext(9));
        services.AddSingleton<PubSoft.Core.IIdGenerator>(PubSoft.Testing.TestIds.Shared);
        services.AddPubSoftEfCoreAuditPersistence<TestDbContext>();
        await using var provider = services.BuildServiceProvider();

        var context = provider.GetRequiredService<TestDbContext>();
        await context.Database.EnsureCreatedAsync();

        var writer = provider.GetRequiredService<IAuditLogWriter>();
        await writer.WriteAsync(new AuditLogEntry(
            new AuditBusinessContext("Auth", "MachineLogin", "Client", "svc-1"),
            Actor: new AuditActorContext("svc-1", "billing-svc")));

        var record = Assert.Single(await context.Set<AuditLogRecord>().AsNoTracking().ToListAsync());
        Assert.Equal("Auth", record.Category);
        Assert.Equal("MachineLogin", record.Action);
        Assert.Equal("svc-1", record.TargetId);
        Assert.Equal(9, record.TenantId);
        Assert.True(record.Success);
    }

    private sealed class RecordingSink : IAuditSnapshotSink
    {
        public List<AuditRequestSnapshot> Snapshots { get; } = [];
        public ValueTask WriteAsync(AuditRequestSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftAuditPersistence();
        }
    }
}
