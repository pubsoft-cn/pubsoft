using PubSoft.Authorization;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuthorizationWaveTests
{
    private sealed class DefProvider(params PermissionDefinition[] defs) : IPermissionDefinitionProvider
    {
        public IReadOnlyList<PermissionDefinition> GetDefinitions() => defs;
    }

    private sealed class GrantProvider(IReadOnlyList<PermissionGrant> grants) : IPermissionGrantProvider
    {
        public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(long subjectId, long? tenantId, CancellationToken ct = default)
            => Task.FromResult(grants);
        public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(long subjectId, string clientCode, CancellationToken ct = default)
            => Task.FromResult(grants);
    }

    private static PermissionDefinition Def(string key, PermissionFlags flags = PermissionFlags.Query)
        => new() { ModuleKey = key, Flags = flags, DisplayName = key };

    [Fact]
    public void GrantCtorRejectsInvalidTenant()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PermissionGrant(0, "m", PermissionFlags.Query));

    [Fact]
    public void GrantDefaults()
    {
        var g = new PermissionGrant(null, "kms", PermissionFlags.Query | PermissionFlags.Modify);
        Assert.Null(g.TenantId);
        Assert.Equal("kms", g.ModuleKey);
        Assert.Equal(0u, g.ExtensionFlags);
        Assert.Empty(g.CustomPermissions);
    }

    [Fact]
    public void RegistryAggregatesProviderDefinitions()
    {
        var reg = new PermissionDefinitionRegistry([new DefProvider(Def("a"), Def("b"))]);
        Assert.Equal(2, reg.AllDefinitions.Count);
    }

    [Fact]
    public async Task IsGrantedRequiresModuleGrant()
    {
        var reg = new PermissionDefinitionRegistry([new DefProvider(Def("kms"))]);
        var svc = new PermissionAuthorizationService(reg, new GrantProvider([new PermissionGrant(1, "kms", PermissionFlags.Query)]));
        Assert.True(await svc.IsGrantedAsync(1, 1, "kms", PermissionFlags.Query));
        Assert.False(await svc.IsGrantedAsync(1, 1, "other", PermissionFlags.Query)); // no grant for module
    }

    [Fact]
    public async Task IsGrantedRequiresSufficientFlags()
    {
        var reg = new PermissionDefinitionRegistry([new DefProvider(Def("kms"))]);
        var svc = new PermissionAuthorizationService(reg, new GrantProvider([new PermissionGrant(1, "kms", PermissionFlags.Query)]));
        Assert.False(await svc.IsGrantedAsync(1, 1, "kms", PermissionFlags.Modify)); // read grant insufficient for write
    }
}
