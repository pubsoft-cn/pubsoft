using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Auditing.Outbox;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using PubSoft.Messaging.Outbox;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditOutboxTests
{
    [Fact]
    public async Task SnapshotSinkStagesAndPersistsTenantScopedOutboxEvent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<TestAuditDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ITenantContext>(new TestTenantContext(42));
        services.AddPubSoftOutbox<TestAuditDbContext>();
        services.AddSingleton<PubSoft.Core.IIdGenerator>(PubSoft.Testing.TestIds.Shared);
        services.AddPubSoftEfCoreAuditPersistence<TestAuditDbContext>();
        services.AddPubSoftAuditOutbox<TestAuditDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
        await context.Database.EnsureCreatedAsync();

        var snapshot = new AuditRequestSnapshot(
            new AuditRequestContext(DateTimeOffset.UtcNow, "POST", "/payments"),
            new AuditBusinessContext("payments", "transfer", "Account", "account-7"),
            true,
            200,
            null,
            TimeSpan.FromMilliseconds(12),
            Array.Empty<AuditEntityChange>());

        var sink = scope.ServiceProvider.GetRequiredService<IAuditSnapshotSink>();
        Assert.IsType<OutboxAuditSnapshotSink<TestAuditDbContext>>(sink);
        await sink.WriteAsync(snapshot);

        var message = await context.Set<OutboxMessage>().SingleAsync();
        Assert.Equal(42, message.TenantId);
        Assert.Equal(OutboxAuditSnapshotSink<TestAuditDbContext>.TargetModule, message.TargetModule);
        Assert.Equal(typeof(AuditSnapshotOutboxEvent).FullName, message.EventType);
        var payload = System.Text.Json.JsonSerializer.Deserialize<AuditSnapshotOutboxEvent>(message.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal(1, payload.SchemaVersion);
        Assert.Equal("transfer", payload.Snapshot.Business.Action);
        Assert.Empty(await context.Set<AuditLogRecord>().ToListAsync());
    }

    private sealed class TestAuditDbContext(
        DbContextOptions<TestAuditDbContext> options,
        ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftOutbox();
            modelBuilder.ConfigurePubSoftAuditPersistence();
        }
    }

    private sealed class TestTenantContext(long? tenantId) : ITenantContext
    {
        public long? TenantId { get; } = tenantId;
    }
}