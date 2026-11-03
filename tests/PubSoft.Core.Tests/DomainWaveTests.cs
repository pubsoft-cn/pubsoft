using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DomainWaveTests
{
    private sealed class FullEntity : FullAuditedAggregateRoot { }
    private sealed class AuditedEntity : AuditedAggregateRoot { }
    private sealed class CreationEntity : CreationAuditedAggregateRoot { }
    private sealed class CreationEntityInt : CreationAuditedEntity<int> { }

    [Fact]
    public void FullAuditedRootCarriesAllAuditFields()
    {
        var e = new FullEntity { TenantId = 7, CreatorId = 9, LastModifierId = 11 };
        Assert.Equal(7, e.TenantId);
        Assert.Equal(9, e.CreatorId);
        Assert.Equal(11, e.LastModifierId);
        Assert.NotEqual(default, e.CreationTime);
        Assert.Null(e.LastModificationTime);
    }

    [Fact]
    public void AuditedRootHasNoSoftDelete()
    {
        var e = new AuditedEntity();
        Assert.False(e is ISoftDelete);
        Assert.True(e is IMultiTenant);
        Assert.Null(e.TenantId);
    }

    [Fact]
    public void CreationAuditedRootOnlyCreateFields()
    {
        var e = new CreationEntity { CreatorId = 5 };
        Assert.Equal(5, e.CreatorId);
        Assert.Null(e.TenantId);
        Assert.False(e is ISoftDelete);
    }

    [Fact]
    public void CreationAuditedEntityGenericKey()
    {
        var e = new CreationEntityInt { CreatorId = 3 };
        Assert.Equal(3, e.CreatorId);
        Assert.NotEqual(default, e.CreationTime);
    }

    [Fact]
    public void IRoleDataScopeProviderIsPublicContract()
        => Assert.True(typeof(IRoleDataScopeProvider).IsInterface);
}
