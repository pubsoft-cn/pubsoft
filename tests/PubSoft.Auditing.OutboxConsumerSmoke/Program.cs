using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Auditing.Outbox;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using PubSoft.Messaging.Outbox;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var services = new ServiceCollection();
services.AddDbContext<SmokeDbContext>(options => options.UseSqlite(connection));
services.AddSingleton<ITenantContext, SmokeTenantContext>();
services.AddPubSoftOutbox<SmokeDbContext>();
services.AddSingleton<PubSoft.Core.IIdGenerator>(new PubSoft.Core.SnowflakeIdGenerator(1));
services.AddPubSoftEfCoreAuditPersistence<SmokeDbContext>();
services.AddPubSoftAuditOutbox<SmokeDbContext>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var context = scope.ServiceProvider.GetRequiredService<SmokeDbContext>();
await context.Database.EnsureCreatedAsync();

var snapshot = new AuditRequestSnapshot(
    new AuditRequestContext(DateTimeOffset.Parse("2026-09-30T12:00:00Z"), "POST", "/payments"),
    new AuditBusinessContext("payments", "transfer", "Account", "account-7"),
    true,
    200,
    null,
    TimeSpan.FromMilliseconds(12),
    Array.Empty<AuditEntityChange>());
var sink = scope.ServiceProvider.GetRequiredService<IAuditSnapshotSink>();
if (sink is not OutboxAuditSnapshotSink<SmokeDbContext>)
    throw new InvalidOperationException("Audit Outbox sink registration check failed.");

await sink.WriteAsync(snapshot);
var message = await context.Set<OutboxMessage>().SingleAsync();
var payload = System.Text.Json.JsonSerializer.Deserialize<AuditSnapshotOutboxEvent>(message.PayloadJson);
if (message.TenantId != 42
    || message.TargetModule != OutboxAuditSnapshotSink<SmokeDbContext>.TargetModule
    || message.EventType != typeof(AuditSnapshotOutboxEvent).FullName
    || payload is not { SchemaVersion: 1 }
    || payload.Snapshot.Business.Action != "transfer"
    || await context.Set<AuditLogRecord>().AnyAsync())
    throw new InvalidOperationException("Audit Outbox persistence consumer check failed.");

Console.WriteLine("Isolated PubSoft.Auditing.Outbox consumer persistence check passed.");

sealed class SmokeDbContext(DbContextOptions<SmokeDbContext> options, ITenantContext tenantContext)
    : PubSoftDbContext(options, tenantContext)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigurePubSoftOutbox();
        modelBuilder.ConfigurePubSoftAuditPersistence();
    }
}

sealed class SmokeTenantContext : ITenantContext
{
    public long? TenantId => 42;
}