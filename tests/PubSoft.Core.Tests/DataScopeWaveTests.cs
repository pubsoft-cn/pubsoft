using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DataScopeWaveTests
{
    private sealed class Registry(params string[] names) : IDataScopeDimensionRegistry
    {
        public IReadOnlyList<string> Dimensions => names;
        public void Add(string dimension) { }
        public bool Contains(string name) => names.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static DataScopeAccessEvaluator Eval(string[] dims) => new(new Registry(dims));
    private static DataScopeDimension Dim(string n, bool all, params long[] ids) => new(n, all, ids);

    [Fact]
    public void GetAllowedIdsUnknownDimensionReturnsEmpty()
    {
        var e = Eval(["plant"]);
        var ids = e.GetAllowedIds("unknown", [Dim("plant", false, 1, 2)]);
        Assert.NotNull(ids);
        Assert.Empty(ids); // unknown dimension → empty set, NOT null (fail-closed)
    }

    [Fact]
    public void GetAllowedIdsIsAllReturnsNullUnrestricted()
    {
        var e = Eval(["plant"]);
        var ids = e.GetAllowedIds("plant", [Dim("plant", true)]);
        Assert.Null(ids); // unrestricted
    }

    [Fact]
    public void GetAllowedIdsUnionsAndDeduplicates()
    {
        var e = Eval(["plant"]);
        var ids = e.GetAllowedIds("plant", [Dim("plant", false, 3, 1), Dim("PLANT", false, 2, 1)]);
        Assert.Equal([1L, 2L, 3L], ids); // union + dedup + ordered + case-insensitive dimension match
    }

    [Fact]
    public void CanAccessFailClosedBoundaries()
    {
        var e = Eval(["plant"]);
        Assert.False(e.CanAccess("plant", 0, [Dim("plant", true)]));   // scopeId<=0 → deny
        Assert.False(e.CanAccess("plant", -5, [Dim("plant", true)]));
        Assert.True(e.CanAccess("plant", 99, [Dim("plant", true)]));   // IsAll → any id
        Assert.True(e.CanAccess("plant", 5, [Dim("plant", false, 5, 7)]));
        Assert.False(e.CanAccess("plant", 6, [Dim("plant", false, 5, 7)])); // outside list → deny
        Assert.False(e.CanAccess("unknown", 5, [Dim("plant", true)])); // unknown dimension → deny
    }

    [Fact]
    public void RoleDataScopeFields()
    {
        var s = new RoleDataScope { RoleId = 1, Dimension = "plant", ScopeId = 5, IsAll = false, TenantId = 2 };
        Assert.Equal(1, s.RoleId);
        Assert.Equal("plant", s.Dimension);
        Assert.Equal(5, s.ScopeId);
        Assert.False(s.IsAll);
        Assert.Equal(2, s.TenantId);
    }
}
