using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DomainWave2Tests
{
    private sealed class LongEntity : Entity<long> { }
    private sealed class StrEntity : Entity<string> { }
    private sealed class AuditedE : AuditedEntity<long> { }
    private sealed class FullE : FullAuditedEntity<long> { }

    [Fact]
    public void EntityIdAssignable()
    {
        var e = new LongEntity { Id = 42 };
        Assert.Equal(42, e.Id);
        var s = new StrEntity { Id = "x" };
        Assert.Equal("x", s.Id);
    }

    [Fact]
    public void EntityEqualityById()
    {
        var a = new LongEntity { Id = 1 };
        var b = new LongEntity { Id = 1 };
        var c = new LongEntity { Id = 2 };
        Assert.Equal(a, b);       // same id → equal
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void AuditedEntityCarriesAuditFields()
    {
        var e = new AuditedE { CreatorId = 5, LastModifierId = 9 };
        Assert.Equal(5, e.CreatorId);
        Assert.Equal(9, e.LastModifierId);
        Assert.NotEqual(default, e.CreationTime);
        Assert.Null(e.LastModificationTime);
        Assert.IsNotAssignableFrom<ISoftDelete>(e); // AuditedEntity has no soft-delete
    }

    [Fact]
    public void FullAuditedEntitySoftDeleteFields()
    {
        var e = new FullE { CreatorId = 1, IsDeleted = true, DeleterId = 3 };
        Assert.True(e.IsDeleted);
        Assert.Equal(3, e.DeleterId);
        Assert.True(e is ISoftDelete);
    }

    [Fact]
    public void PermissionModuleCtorAndUpdate()
    {
        var m = new PermissionModule(1, "cc", "kms", "KMS", sortOrder: 5);
        Assert.Equal("cc", m.ClientCode);
        Assert.Equal("kms", m.ModuleKey);
        Assert.Equal("KMS", m.DisplayName);
        Assert.Equal(5, m.SortOrder);
        Assert.True(m.IsEnabled);
        Assert.Equal((byte)63, m.AvailableFlags);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PermissionModule(0, "cc", "k", "d"));
        m.Update("KMS v2", "new desc", 15, null, 10);
        Assert.Equal("KMS v2", m.DisplayName);
        Assert.Equal("new desc", m.Description);
        Assert.Equal((byte)15, m.AvailableFlags);
        Assert.Equal(10, m.SortOrder);
        Assert.NotNull(m.UpdatedAt);
    }
}
