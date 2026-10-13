using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DataScopeDimensionRegistryTests
{
    [Fact]
    public void RegistryIsCaseInsensitiveAndReturnsAnIsolatedSnapshot()
    {
        IDataScopeDimensionRegistry registry = new DataScopeDimensionRegistry();

        registry.Add("store");
        registry.Add("Store");
        var dimensions = registry.Dimensions;
        registry.Add("device");

        Assert.True(registry.Contains("STORE"));
        Assert.Single(dimensions);
        Assert.Equal(["device", "store"], registry.Dimensions.OrderBy(dimension => dimension));
        Assert.Equal("store", dimensions[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)dimensions).Add("plant"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AddRejectsEmptyDimensionNames(string dimension)
    {
        var registry = new DataScopeDimensionRegistry();

        Assert.Throws<ArgumentException>(() => registry.Add(dimension));
        Assert.Empty(registry.Dimensions);
    }

    [Fact]
    public void RegistrySupportsConcurrentAddsAndReads()
    {
        var registry = new DataScopeDimensionRegistry();
        var dimensions = Enumerable.Range(0, 64)
            .Select(index => $"scope-{index:D2}")
            .ToArray();

        Parallel.ForEach(dimensions, dimension =>
        {
            registry.Add(dimension);
            Assert.True(registry.Contains(dimension));
        });

        Assert.Equal(dimensions.OrderBy(dimension => dimension), registry.Dimensions.OrderBy(dimension => dimension));
    }

    [Fact]
    public void DataScopeDimensionCopiesAllowedIdsWithoutInterpretingScopePolicy()
    {
        var allowedIds = new List<long> { 4, 9 };

        var dimension = new DataScopeDimension("store", false, allowedIds, currentId: 9);
        allowedIds.Add(15);

        Assert.Equal("store", dimension.Name);
        Assert.False(dimension.IsAll);
        Assert.Equal([4L, 9L], dimension.AllowedIds);
        Assert.Equal(9, dimension.CurrentId);
        Assert.Throws<NotSupportedException>(() => ((IList<long>)dimension.AllowedIds).Add(15));
    }

    [Fact]
    public void DataScopeEvaluatorUnionsRoleAllowListsForRegisteredDimensions()
    {
        var registry = new DataScopeDimensionRegistry();
        registry.Add("store");
        var evaluator = new DataScopeAccessEvaluator(registry);
        var roleScopes = new[]
        {
            new DataScopeDimension("store", false, [4, 9], currentId: 9),
            new DataScopeDimension("STORE", false, [9, 15])
        };

        Assert.Equal([4L, 9L, 15L], evaluator.GetAllowedIds("store", roleScopes));
        Assert.True(evaluator.CanAccess("STORE", 4, roleScopes));
        Assert.True(evaluator.CanAccess("store", 15, roleScopes));
        Assert.False(evaluator.CanAccess("store", 16, roleScopes));
    }

    [Fact]
    public void DataScopeEvaluatorFailsClosedForMissingUnknownAndInvalidScopes()
    {
        var registry = new DataScopeDimensionRegistry();
        registry.Add("store");
        var evaluator = new DataScopeAccessEvaluator(registry);

        Assert.Empty(evaluator.GetAllowedIds("store", null)!);
        Assert.Empty(evaluator.GetAllowedIds("device", [new DataScopeDimension("device", true)])!);
        Assert.False(evaluator.CanAccess("store", 1, null));
        Assert.False(evaluator.CanAccess("store", 0, [new DataScopeDimension("store", true)]));
        Assert.False(evaluator.CanAccess("device", 1, [new DataScopeDimension("device", true)]));
    }

    [Fact]
    public void DataScopeEvaluatorTreatsIsAllAsDimensionSpecificAcrossRoles()
    {
        var registry = new DataScopeDimensionRegistry();
        registry.Add("store");
        registry.Add("device");
        var evaluator = new DataScopeAccessEvaluator(registry);
        var roleScopes = new[]
        {
            new DataScopeDimension("store", false, [4]),
            new DataScopeDimension("store", true),
            new DataScopeDimension("device", false, [8])
        };

        Assert.Null(evaluator.GetAllowedIds("store", roleScopes));
        Assert.True(evaluator.CanAccess("store", 99, roleScopes));
        Assert.Equal([8L], evaluator.GetAllowedIds("device", roleScopes));
        Assert.False(evaluator.CanAccess("device", 99, roleScopes));
    }
}