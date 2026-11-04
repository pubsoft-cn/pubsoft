using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DomainTailWave3Tests
{
    private sealed class StrEntity : Entity<string> { }
    private sealed class AudE : AuditedEntity<Guid> { }
    private sealed class FullE : FullAuditedEntity<int> { }
    private sealed class CreateE : CreationAuditedEntity<long> { }

    [Fact]
    public void GenericEntityIdAndEquality()
    {
        var a = new StrEntity { Id = "x" };
        var b = new StrEntity { Id = "x" };
        Assert.Equal("x", a.Id);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new StrEntity { Id = "y" });
    }

    [Fact]
    public void GenericAuditedEntityFields()
    {
        var e = new AudE { Id = Guid.NewGuid(), CreatorId = 1, LastModifierId = 2 };
        Assert.Equal(1, e.CreatorId);
        Assert.NotEqual(default, e.CreationTime);
        Assert.Equal(2, e.LastModifierId);
        Assert.Null(e.LastModificationTime);
        e.LastModificationTime = DateTime.UtcNow;
        Assert.NotNull(e.LastModificationTime);
    }

    [Fact]
    public void GenericFullAuditedEntitySoftDelete()
    {
        var e = new FullE { Id = 7, IsDeleted = true, DeleterId = 9, DeletionTime = DateTime.UtcNow };
        Assert.True(e.IsDeleted);
        Assert.Equal(9, e.DeleterId);
        Assert.NotNull(e.DeletionTime);
        Assert.Equal(7, e.Id);
    }

    [Fact]
    public void GenericCreationAuditedEntity()
    {
        var e = new CreateE { Id = 1, CreatorId = 5 };
        Assert.Equal(5, e.CreatorId);
        Assert.NotEqual(default, e.CreationTime);
    }

    [Fact]
    public void PermissionModuleCreationTimeAndGrantFields()
    {
        var m = new PermissionModule(1, "cc", "mod", "Mod");
        Assert.NotEqual(default, m.CreationTime);
        var g = new PermissionGrantRecord(1, 2, "kms", flags: 3, extensionFlags: 0, customPermissionName: null);
        Assert.Equal(2, g.RoleId);
        Assert.Equal("kms", g.ModuleKey);
        Assert.Null(g.CustomPermissionName);
    }
}
