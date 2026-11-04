using PubSoft.Authorization;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuthorizationTailWaveTests
{
    [Fact]
    public void DiscoveredPermissionModuleDefaults()
    {
        var m = new DiscoveredPermissionModule { ModuleKey = "kms", DisplayName = "KMS" };
        Assert.Equal("kms", m.ModuleKey);
        Assert.Equal("KMS", m.DisplayName);
        Assert.Equal((byte)63, m.AvailableFlags);
        Assert.Empty(m.CustomPermissions);
        Assert.Empty(m.ApiPaths);
        Assert.Equal(0, m.SortOrder);
    }

    [Fact]
    public void DiscoveredPermissionModuleRecordSemantics()
    {
        var a = new DiscoveredPermissionModule { ModuleKey = "a", DisplayName = "A" };
        var b = a with { };
        Assert.Equal(a, b);
        // NOTE: CustomPermissions/ApiPaths are List<T> — record equality compares by reference,
        // so two modules with identical field values but separate lists are NOT equal
        var c = new DiscoveredPermissionModule { ModuleKey = "a", DisplayName = "A", CustomPermissions = ["x"] };
        var d = new DiscoveredPermissionModule { ModuleKey = "a", DisplayName = "A", CustomPermissions = ["x"] };
        Assert.NotEqual(c, d);
    }

    [Fact]
    public void DiscoveryOptionsResolversAndRouteMap()
    {
        var o = new PermissionModuleDiscoveryOptions();
        Assert.Empty(o.RouteModuleMap);
        o.RouteModuleMap["api/kms"] = "kms";
        Assert.Equal("kms", o.RouteModuleMap["API/KMS"]); // case-insensitive
        Assert.Null(o.ModuleKeyResolver);
        Assert.Null(o.PinDisplayNameResolver);
        Assert.Null(o.PinSortOrderResolver);
        Assert.Null(o.PinAvailableFlagsResolver);
        Assert.Null(o.CustomPermissionsExtractor);
    }

    [Fact]
    public void PermissionDefinitionRecordFields()
    {
        var d = new PermissionDefinition
        {
            ClientCode = "cc",
            ModuleKey = "m",
            Flags = PermissionFlags.Query | PermissionFlags.Modify,
            DisplayName = "Module",
            Description = "desc",
            ApiPaths = ["/api/x"],
            IsExtPermission = true,
            ExtFlag = 4,
        };
        Assert.Equal("cc", d.ClientCode);
        Assert.Equal(PermissionFlags.Query | PermissionFlags.Modify, d.Flags);
        Assert.Equal("desc", d.Description);
        Assert.Single(d.ApiPaths);
        Assert.True(d.IsExtPermission);
        Assert.Equal(4u, d.ExtFlag);
    }

    [Fact]
    public void PermissionGrantClientCodeAndFlags()
    {
        var g = new PermissionGrant(1, "m", PermissionFlags.Delete, 7, ["custom.p"], "client-a");
        Assert.Equal("client-a", g.ClientCode);
        Assert.Equal(PermissionFlags.Delete, g.Flags);
        Assert.Equal(7u, g.ExtensionFlags);
        Assert.Single(g.CustomPermissions);
    }
}
