using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboxTailWave2Tests
{
    private sealed class Tenant : ITenantContext { public long? TenantId => 1; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.ConfigurePubSoftOutbox();
        }
    }

    [Fact]
    public void AddPubSoftOutboxRegistersStore()
    {
        var s = new ServiceCollection();
        s.AddDbContext<Ctx>(o => o.UseSqlite("Data Source=:memory:"));
        s.AddPubSoftOutbox<Ctx>();
        Assert.Contains(s, d => d.ServiceType == typeof(IOutboxStore));
        Assert.Contains(s, d => d.ServiceType == typeof(IOutboxPublisher));
    }

    [Fact]
    public async Task AddPubSoftOutboxResolvesRegisteredEventSerializer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<Ctx>(o => o.UseSqlite(connection));
        services.AddSingleton<PubSoft.Core.IIdGenerator>(PubSoft.Testing.TestIds.Shared);
        services.AddSingleton<IOutboxEventSerializer>(new OutboxEventJsonSerializer(new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        }));
        services.AddPubSoftOutbox<Ctx>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<Ctx>();
        await ctx.Database.EnsureCreatedAsync();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        var id = await publisher.PublishAsync(new SerializerProbeEvent(42), eventType: "tests.serializer-probe.v1");
        await ctx.SaveChangesAsync();

        var row = await ctx.Set<OutboxMessage>().FindAsync(id);
        Assert.Contains("\"orderId\"", row!.PayloadJson);
        Assert.DoesNotContain("\"OrderId\"", row.PayloadJson);
    }

    private sealed record SerializerProbeEvent(int OrderId);

    [Fact]
    public void AddPubSoftOutboxWorkerRegistersHosted()
    {
        var s = new ServiceCollection();
        s.AddDbContext<Ctx>(o => o.UseSqlite("Data Source=:memory:"));
        s.AddPubSoftOutbox<Ctx>().AddPubSoftOutboxWorker();
        Assert.Contains(s, d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
    }

    [Fact]
    public void OutboxMessageFullMembers()
    {
        var m = (OutboxMessage)typeof(OutboxMessage)
            .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                new[] { typeof(long), typeof(string), typeof(string), typeof(string), typeof(string), typeof(DateTimeOffset), typeof(int), typeof(string) }, null)!
            .Invoke(new object?[] { PubSoft.Testing.TestIds.Next(), "type", "{}", "mod", "agg", DateTimeOffset.UtcNow, 5, "corr-1" });
        Assert.Equal("mod", m.TargetModule);
        Assert.Equal("agg", m.AggregateKey);
        Assert.Equal("corr-1", m.CorrelationId);
        Assert.Equal(5, m.MaxAttempts);
        Assert.Null(m.NextAttemptAt);
        Assert.Null(m.LeaseExpiresAt);
        Assert.Null(m.LastErrorCode);
        Assert.NotEqual(0L, m.Id);
    }

    [Fact]
    public void InboxReceiptMembers()
    {
        var r = (InboxReceipt)typeof(InboxReceipt)
            .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                new[] { typeof(long?), typeof(string), typeof(long), typeof(DateTimeOffset) }, null)!
            .Invoke(new object?[] { 7L, "consumer", PubSoft.Testing.TestIds.Next(), DateTimeOffset.UtcNow });
        Assert.Equal("consumer", r.ConsumerName);
        Assert.Equal(7L, r.TenantId);
        Assert.NotEqual(default, r.ProcessedAt);
        Assert.NotEqual(0L, r.MessageId);
    }
}
