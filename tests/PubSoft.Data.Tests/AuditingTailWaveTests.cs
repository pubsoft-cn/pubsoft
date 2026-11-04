using PubSoft.Auditing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditingTailWaveTests
{
    [Fact]
    public void CollectorOptionsDefaultsAndSensitiveSet()
    {
        var o = new AuditChangeCollectorOptions();
        Assert.Equal(1_000, o.MaxChanges);
        Assert.Equal(50, o.MaxChangedPropertiesPerEntity);
        Assert.Equal(2_048, o.MaxStringValueLength);
        Assert.NotEmpty(o.SensitiveProperties);
        var v = o.CloneAndValidate();
        Assert.Equal(o.MaxChanges, v.MaxChanges);
    }

    [Fact]
    public void CollectorOptionsInvalidMaxRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuditChangeCollectorOptions { MaxChanges = 0 }.CloneAndValidate());

    [Fact]
    public void AuditPropertyChangeRecord()
    {
        var p = new AuditPropertyChange("F", null, null);
        Assert.Equal("F", p.Name);
        Assert.Null(p.OldValue);
        Assert.Equal(p, new AuditPropertyChange("F", null, null));
    }

    [Fact]
    public void AuditEntityChangeRecordMembers()
    {
        var props = new List<AuditPropertyChange> { new("A", null, null) };
        var e = new AuditEntityChange("T", "1", AuditChangeKind.Updated, DateTimeOffset.UnixEpoch, props);
        Assert.Equal("T", e.EntityType);
        Assert.Equal(AuditChangeKind.Updated, e.Kind);
        Assert.Equal(DateTimeOffset.UnixEpoch, e.ChangedAt);
        Assert.Single(e.Properties);
        Assert.Equal("Created", AuditChangeKind.Created.ToString());
    }

    [Fact]
    public void AuditLogEntryRecordMembers()
    {
        var e = new AuditLogEntry(
            new AuditBusinessContext("c"), null, false, "E1",
            TimeSpan.FromSeconds(1));
        Assert.False(e.Success);
        Assert.Equal("E1", e.FailureCode);
        Assert.Equal(TimeSpan.FromSeconds(1), e.Duration);
    }

    [Fact]
    public void AuditScopeOptionsDefaults()
    {
        var o = new AuditScopeOptions();
        Assert.True(o.Enabled);
        Assert.False(o.AuditGetRequests);
    }
}
