using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<SmokeDbContext>().UseSqlite(connection).Options;
await using var dbContext = new SmokeDbContext(options);
await dbContext.Database.EnsureCreatedAsync();

var publisher = new EfCoreOutboxPublisher<SmokeDbContext>(dbContext);
Guid rolledBackId;
await using (var transaction = await dbContext.Database.BeginTransactionAsync())
{
    rolledBackId = await publisher.PublishAsync(new SmokeEvent("rolled-back", "created"));
    await dbContext.SaveChangesAsync();
    await transaction.RollbackAsync();
}

dbContext.ChangeTracker.Clear();
if (await dbContext.Set<OutboxMessage>().AnyAsync(message => message.Id == rolledBackId))
    throw new InvalidOperationException("Outbox message survived rollback of the business transaction.");

var committedId = await publisher.PublishAsync(
    new SmokeEvent("order-42", "created"),
    targetModule: "billing",
    aggregateKey: "order-42");
await dbContext.SaveChangesAsync();
var persisted = await dbContext.Set<OutboxMessage>().SingleAsync(message => message.Id == committedId);
using var payload = JsonDocument.Parse(persisted.PayloadJson);
if (persisted.TenantId != 17
    || persisted.Status != OutboxMessageStatus.Pending
    || persisted.EventType != typeof(SmokeEvent).FullName
    || persisted.TargetModule != "billing"
    || persisted.AggregateKey != "order-42"
    || payload.RootElement.GetProperty(nameof(SmokeEvent.OrderId)).GetString() != "order-42")
    throw new InvalidOperationException("Outbox message persistence smoke check failed.");

var inbox = new EfCoreInboxMessageProcessor<SmokeDbContext>(dbContext);
var inboxMessageId = Guid.NewGuid();
var failedHandlerCalls = 0;
try
{
    await inbox.ProcessOnceAsync("orders", inboxMessageId, _ =>
    {
        failedHandlerCalls++;
        dbContext.Set<SmokeBusinessRecord>().Add(new SmokeBusinessRecord { Id = 1, Value = "rolled-back" });
        throw new InvalidOperationException("handler failure");
    });
    throw new InvalidOperationException("Inbox should propagate handler failure.");
}
catch (InvalidOperationException exception) when (exception.Message == "handler failure")
{
}

if (failedHandlerCalls != 1
    || await dbContext.Set<InboxReceipt>().AnyAsync(receipt => receipt.MessageId == inboxMessageId)
    || await dbContext.Set<SmokeBusinessRecord>().AnyAsync())
    throw new InvalidOperationException("Inbox failed-handler rollback smoke check failed.");

var successfulHandlerCalls = 0;
var processed = await inbox.ProcessOnceAsync("orders", inboxMessageId, _ =>
{
    successfulHandlerCalls++;
    dbContext.Set<SmokeBusinessRecord>().Add(new SmokeBusinessRecord { Id = 2, Value = "committed" });
    return Task.CompletedTask;
});
var duplicateProcessed = await inbox.ProcessOnceAsync("orders", inboxMessageId, _ =>
{
    successfulHandlerCalls++;
    return Task.CompletedTask;
});
if (!processed
    || duplicateProcessed
    || successfulHandlerCalls != 1
    || await dbContext.Set<SmokeBusinessRecord>().CountAsync() != 1
    || await dbContext.Set<InboxReceipt>().CountAsync(receipt => receipt.MessageId == inboxMessageId) != 1)
    throw new InvalidOperationException("Inbox retry and duplicate suppression smoke check failed.");

Console.WriteLine("Isolated PubSoft.Messaging.Outbox consumer smoke check passed.");

sealed record SmokeEvent(string OrderId, string Status);

sealed class SmokeDbContext(DbContextOptions<SmokeDbContext> options)
    : PubSoftDbContext(options, new SmokeTenantContext())
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigurePubSoftOutbox();
        modelBuilder.Entity<SmokeBusinessRecord>().HasKey(record => record.Id);
    }
}

sealed class SmokeBusinessRecord
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
}

sealed class SmokeTenantContext : ITenantContext
{
    public long? TenantId => 17;
}