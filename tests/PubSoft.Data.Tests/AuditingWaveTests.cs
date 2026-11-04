using PubSoft.Auditing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditingWaveTests
{
    private static AuditEntityChange Change(string type = "E", AuditChangeKind kind = AuditChangeKind.Created)
        => new(type, "1", kind, DateTimeOffset.UtcNow, []);

    [Fact]
    public void CollectorThrowsWhenMaxChangesExceeded()
    {
        // fail-closed: exceeding the cap throws rather than silently truncating
        var c = new AuditChangeCollector(new AuditChangeCollectorOptions { MaxChanges = 2 });
        var ex = Assert.Throws<InvalidOperationException>(() =>
            c.AddRange([Change("a"), Change("b"), Change("c")]));
        Assert.Contains("limit of 2", ex.Message);
    }

    [Fact]
    public void CollectorAcceptsUpToCap()
    {
        var c = new AuditChangeCollector(new AuditChangeCollectorOptions { MaxChanges = 2 });
        c.AddRange([Change("a"), Change("b")]);
        Assert.Equal(2, c.GetSnapshot().Count);
    }

    [Fact]
    public void CollectorClearAndSnapshot()
    {
        var c = new AuditChangeCollector();
        c.Add(Change());
        Assert.Single(c.GetSnapshot());
        c.Clear();
        Assert.Empty(c.GetSnapshot());
    }

    [Fact]
    public void SerializerTruncatesLongStrings()
    {
        var el = AuditValueSerializer.Serialize(new string('x', 100), 10);
        Assert.NotNull(el);
        var s = el!.Value.GetString()!;
        Assert.True(s.EndsWith("..."));
        Assert.Equal(13, s.Length); // 10 chars + "..."
    }

    [Fact]
    public void SerializerFiltersToPrimitives()
    {
        Assert.Null(AuditValueSerializer.Serialize(new { Complex = 1 }, 100)); // complex type → null
        Assert.NotNull(AuditValueSerializer.Serialize(42, 100));
        Assert.NotNull(AuditValueSerializer.Serialize(3.14m, 100));
        Assert.NotNull(AuditValueSerializer.Serialize(Guid.NewGuid(), 100));
        Assert.NotNull(AuditValueSerializer.Serialize(DateTimeOffset.UtcNow, 100));
        Assert.NotNull(AuditValueSerializer.Serialize(null, 100));
    }

    private sealed class CaptureSink : IAuditSnapshotSink
    {
        public List<AuditRequestSnapshot> Snapshots { get; } = [];
        public ValueTask WriteAsync(AuditRequestSnapshot snapshot, CancellationToken ct = default)
        {
            Snapshots.Add(snapshot);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task LogWriterRequiresBusiness()
    {
        var w = new AuditLogWriter(new CaptureSink());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            w.WriteAsync(new AuditLogEntry(Business: null!)));
    }

    [Fact]
    public async Task LogWriterWritesSnapshotToSink()
    {
        var sink = new CaptureSink();
        var w = new AuditLogWriter(sink);
        await w.WriteAsync(new AuditLogEntry(
            new AuditBusinessContext(Category: "billing", Action: "charge", Detail: "paid"),
            Success: true, Duration: TimeSpan.FromMilliseconds(50)));
        Assert.Single(sink.Snapshots);
        var s = sink.Snapshots[0];
        Assert.Equal("billing", s.Business.Category);
        Assert.Equal("charge", s.Business.Action);
        Assert.True(s.Success);
    }

    [Fact]
    public async Task LogWriterAppendsMetricsToDetail()
    {
        var sink = new CaptureSink();
        var w = new AuditLogWriter(sink);
        await w.WriteAsync(new AuditLogEntry(
            new AuditBusinessContext(Detail: "base"),
            Metrics: new Dictionary<string, string> { ["retries"] = "3" }));
        Assert.Contains("retries", sink.Snapshots[0].Business.Detail);
    }

    [Fact]
    public void AuditEntityChangeRecordShape()
    {
        var c = Change("Order", AuditChangeKind.Updated);
        Assert.Equal("Order", c.EntityType);
        Assert.Equal("1", c.EntityId);
        Assert.Equal(AuditChangeKind.Updated, c.Kind);
    }
}
