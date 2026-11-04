using PubSoft.Authorization;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuthorizationTailWave2Tests
{
    private sealed class Mod : IPermissionModuleProvider
    {
        public IReadOnlyCollection<string> All => ["kms", "order"];
        public string GetDisplayName(string k) => k.ToUpperInvariant();
        public string? GetDescription(string k) => $"desc:{k}";
        public int? GetSortOrder(string k) => 10;
        public byte? GetAvailableFlags(string k) => 15;
    }

    [Fact]
    public void RegistryAggregatesProviders()
    {
        var r = new PermissionModuleRegistry([new Mod()]);
        Assert.Equal(2, r.AllModules.Length);
        Assert.Equal("KMS", r.GetDisplayName("kms"));
        Assert.Equal("desc:kms", r.GetDescription("kms"));
        r.Register(new Mod());
        Assert.Equal(2, r.AllModules.Length); // AllModules dedups by module key
    }

    [Fact]
    public void RegistryFallsBackToKey()
    {
        var r = new PermissionModuleRegistry();
        Assert.Equal("unknown", r.GetDisplayName("unknown")); // no provider → key passthrough
        Assert.Null(r.GetDescription("unknown"));
        Assert.Empty(r.AllModules);
    }

    [Fact]
    public void PermissionModuleAttributeCarriesKey()
    {
        var a = new PermissionModuleAttribute("kms", "cc");
        Assert.Equal("kms", a.ModuleKey);
        Assert.Equal("cc", a.ClientCode);
        var def = new PermissionModuleAttribute("m");
        Assert.Equal(string.Empty, def.ClientCode);
    }
}
