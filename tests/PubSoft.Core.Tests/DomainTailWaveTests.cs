using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DomainTailWaveTests
{
    private sealed class AggRoot : FullAuditedAggregateRoot { }
    private sealed class Agg : AuditedAggregateRoot { }
    private sealed class CAgg : CreationAuditedAggregateRoot { }

    [Fact]
    public void FullAuditedAggregateRootFields()
    {
        var a = new AggRoot
        {
            CreatorId = 1,
            LastModifierId = 2,
            LastModificationTime = DateTime.UtcNow,
            IsDeleted = true,
            DeleterId = 3,
            DeletionTime = DateTime.UtcNow,
        };
        Assert.Equal(1, a.CreatorId);
        Assert.Equal(2, a.LastModifierId);
        Assert.NotNull(a.LastModificationTime);
        Assert.True(a.IsDeleted);
        Assert.Equal(3, a.DeleterId);
        Assert.NotNull(a.DeletionTime);
        Assert.NotEqual(default, a.CreationTime);
    }

    [Fact]
    public void AuditedAggregateRootFields()
    {
        var a = new Agg { CreatorId = 5, LastModifierId = 6 };
        Assert.Equal(5, a.CreatorId);
        Assert.NotEqual(default, a.CreationTime);
        Assert.Null(a.LastModificationTime);
    }

    [Fact]
    public void CreationAuditedAggregateRootFields()
    {
        var a = new CAgg { CreatorId = 9 };
        Assert.Equal(9, a.CreatorId);
        Assert.NotEqual(default, a.CreationTime);
    }

    [Fact]
    public void PermissionRoleEntity()
    {
        var r = new PermissionRole(1, "admin", 7, "cc");
        Assert.Equal(1, r.Id);
        Assert.Equal("admin", r.Name);
        Assert.Equal(7, r.TenantId);
        Assert.Equal("cc", r.ClientCode);
        Assert.True(r.IsEnabled);
        var def = new PermissionRole();
        Assert.True(def.IsEnabled);
    }

    [Fact]
    public void SubjectPermissionRoleEntity()
    {
        var s = new SubjectPermissionRole(1, 42, 5, 7);
        Assert.Equal(1, s.Id);
        Assert.Equal(42, s.SubjectId);
        Assert.Equal(5, s.RoleId);
        Assert.Equal(7, s.TenantId);
    }

    [Fact]
    public void PermissionGrantRecordSetPermissions()
    {
        var g = new PermissionGrantRecord(1, 1, "kms", flags: 15, extensionFlags: 3);
        g.SetPermissions(15, 3, null);
        Assert.Equal((byte)15, g.Flags);
        Assert.Equal(3u, g.ExtensionFlags);
        Assert.Throws<ArgumentException>(() => g.SetPermissions(0, 0, null)); // all-empty grant rejected
    }
}
