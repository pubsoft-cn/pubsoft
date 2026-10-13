using PubSoft.Authorization;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuthorizationModuleTests
{
    [Theory]
    [InlineData("GET", "Read", PermissionFlags.Query)]
    [InlineData("HEAD", "Read", PermissionFlags.Query)]
    [InlineData("DELETE", "Read", PermissionFlags.Delete)]
    [InlineData("PUT", "Read", PermissionFlags.Modify)]
    [InlineData("PATCH", "Read", PermissionFlags.Modify)]
    [InlineData("OPTIONS", "Read", PermissionFlags.Custom)]
    [InlineData(null, null, PermissionFlags.Query)]
    public void DeriveFlagsUsesHttpVerbDefaults(string? method, string? actionName, PermissionFlags expected)
    {
        Assert.Equal(expected, PermissionRequirementDeriver.DeriveFlags(method, actionName));
    }

    [Theory]
    [InlineData("Approve", PermissionFlags.Audit)]
    [InlineData("AuditDelete", PermissionFlags.Audit)]
    [InlineData("DeleteUser", PermissionFlags.Delete)]
    [InlineData("RemoveUser", PermissionFlags.Delete)]
    [InlineData("UpdateUser", PermissionFlags.Modify)]
    [InlineData("CreateUser", PermissionFlags.Create)]
    public void PostActionNamesUseSourcePriority(string actionName, PermissionFlags expected)
    {
        Assert.Equal(expected, PermissionRequirementDeriver.DeriveFlags("POST", actionName));
    }

    [Theory]
    [InlineData("RestaurantStructure", "restaurant_structure")]
    [InlineData("URLValue", "url_value")]
    [InlineData("user", "user")]
    [InlineData(null, "")]
    public void ToSnakeCaseSplitsPascalAndAcronymNames(string? name, string expected)
    {
        Assert.Equal(expected, PermissionRequirementDeriver.ToSnakeCase(name!));
    }

    [Fact]
    public void PinnedModuleTakesPrecedenceOverRouteAndProviderMatches()
    {
        var registry = CreateRegistry("records", "admin_records");
        var module = PermissionRequirementDeriver.ResolveModuleKey(
            "Records", "api/admin/records", "explicit", registry,
            new Dictionary<string, string> { ["api/admin"] = "route" });

        Assert.Equal("explicit", module);
    }

    [Fact]
    public void RouteResolutionUsesLongestSegmentBoundedPrefixCaseInsensitively()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["api"] = "root",
            ["api/admin"] = "admin"
        };

        Assert.Equal("admin", PermissionRequirementDeriver.ResolveModuleKey(null, "API/ADMIN/users", null, null, map));
        Assert.Equal("root", PermissionRequirementDeriver.ResolveModuleKey(null, "api/users", null, null, map));
        Assert.Null(PermissionRequirementDeriver.ResolveModuleKey(null, "apiary/users", null, null, map));
    }

    [Fact]
    public void ProviderResolutionRequiresOneUnambiguousControllerSuffix()
    {
        var unique = CreateRegistry("tenant_records");
        var ambiguous = CreateRegistry("tenant_records", "admin_records");

        Assert.Equal("tenant_records", PermissionRequirementDeriver.ResolveModuleKey("Records", null, null, unique));
        Assert.Null(PermissionRequirementDeriver.ResolveModuleKey("Records", null, null, ambiguous));
    }

    [Fact]
    public void ModuleRegistryAggregatesDistinctKeysAndFallsBackToKey()
    {
        var registry = CreateRegistry("records", "audit", "records");

        Assert.Equal(["records", "audit"], registry.AllModules);
        Assert.Equal("Display records", registry.GetDisplayName("records"));
        Assert.Equal("unknown", registry.GetDisplayName("unknown"));
        Assert.Equal("Record access", registry.GetDescription("records"));
    }

    [Fact]
    public void PermissionMetadataAttributesAreFrameworkIndependent()
    {
        var module = new PermissionModuleAttribute("records") { DisplayName = "Records", SortOrder = 2 };
        var required = new RequirePermissionAttribute("records", PermissionFlags.Query | PermissionFlags.Modify);

        Assert.Equal("records", module.ModuleKey);
        Assert.Equal("Records", module.DisplayName);
        Assert.Equal(PermissionFlags.Query | PermissionFlags.Modify, required.Flags);
        Assert.Equal(AttributeTargets.Method | AttributeTargets.Class,
            Attribute.GetCustomAttribute(typeof(RequirePermissionAttribute), typeof(AttributeUsageAttribute))
                is AttributeUsageAttribute usage ? usage.ValidOn : AttributeTargets.All);
    }

    [Fact]
    public void CustomAndExtensionPermissionAttributesRetainTheirMetadata()
    {
        var custom = new RequireCustomPermissionAttribute("records", "export");
        var extension = new RequireExtPermissionAttribute("records", 4);

        Assert.Equal("records", custom.ModuleKey);
        Assert.Equal("export", custom.PermissionName);
        Assert.Equal("records", extension.ModuleKey);
        Assert.Equal(4u, extension.ExtFlag);
    }

    [Fact]
    public void CustomAndExtensionPermissionAttributesRejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new RequireCustomPermissionAttribute(" ", "export"));
        Assert.Throws<ArgumentException>(() => new RequireCustomPermissionAttribute("records", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequireExtPermissionAttribute("records", 0));
    }

    private static PermissionModuleRegistry CreateRegistry(params string[] keys) =>
        new([new TestModuleProvider(keys)]);

    private sealed class TestModuleProvider(IReadOnlyCollection<string> keys) : IPermissionModuleProvider
    {
        public IReadOnlyCollection<string> All => keys;

        public string GetDisplayName(string moduleKey) => keys.Contains(moduleKey)
            ? $"Display {moduleKey}"
            : moduleKey;

        public string? GetDescription(string moduleKey) => keys.Contains(moduleKey) ? "Record access" : null;
    }
}