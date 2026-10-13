using PubSoft.Authorization;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuthorizationCatalogTests
{
    [Fact]
    public void PermissionFlagsPreserveTheSourceBitValues()
    {
        Assert.Equal((byte)1, (byte)PermissionFlags.Query);
        Assert.Equal((byte)2, (byte)PermissionFlags.Create);
        Assert.Equal((byte)4, (byte)PermissionFlags.Modify);
        Assert.Equal((byte)8, (byte)PermissionFlags.Delete);
        Assert.Equal((byte)16, (byte)PermissionFlags.Audit);
        Assert.Equal((byte)32, (byte)PermissionFlags.Custom);
        Assert.Equal((byte)63, (byte)PermissionFlags.All);
    }

    [Fact]
    public void FrontendAndApiTemplatesMatchArbitraryNamedSegmentsOnly()
    {
        var catalog = CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query,
            DisplayName = "Read users",
            FrontendRoutes = ["/users/:userId"],
            ApiPaths = ["GET /api/users/{userId}"]
        });

        Assert.Single(catalog.GetByFrontendRoute("/users/42"));
        Assert.Empty(catalog.GetByFrontendRoute("/users/42/details"));
        Assert.Single(catalog.GetByApiPath("GET", "/api/users/42"));
        Assert.Single(catalog.GetByApiPath("get", "/api/users/42"));
        Assert.Single(catalog.GetByApiPath("GET", "/api/users/42/"));
        Assert.Empty(catalog.GetByApiPath("POST", "/api/users/42"));
    }

    [Fact]
    public void LiteralRouteSegmentsAreNotInterpretedAsPatterns()
    {
        var catalog = CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "search",
            Flags = PermissionFlags.Query,
            DisplayName = "Read search docs",
            FrontendRoutes = ["/docs/a+b"]
        });

        Assert.Single(catalog.GetByFrontendRoute("/docs/a+b"));
        Assert.Empty(catalog.GetByFrontendRoute("/docs/aaab"));
    }

    [Fact]
    public void RegistrySnapshotsProviderDefinitionsAndRouteCollections()
    {
        var routes = new List<string> { "/users/:userId" };
        var definitions = new List<PermissionDefinition>
        {
            new()
            {
                ModuleKey = "users",
                Flags = PermissionFlags.Query,
                DisplayName = "Read users",
                FrontendRoutes = routes
            }
        };
        var catalog = new PermissionDefinitionRegistry([new TestProvider(definitions)]);

        routes[0] = "/changed";
        definitions.Clear();

        Assert.Single(catalog.GetByFrontendRoute("/users/42"));
        Assert.Single(catalog.AllDefinitions);
    }

    [Fact]
    public void RegistryRejectsDuplicatePermissionKeys()
    {
        var definition = new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query,
            DisplayName = "Read users"
        };

        Assert.Throws<ArgumentException>(() => new PermissionDefinitionRegistry(
        [
            new TestProvider([definition]),
            new TestProvider([definition with { DisplayName = "Read users again" }])
        ]));
    }

    [Fact]
    public void RegistryAllowsSameModuleAndFlagsInSeparateClientNamespaces()
    {
        var definition = new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query,
            DisplayName = "Read users"
        };
        var catalog = new PermissionDefinitionRegistry(
        [
            new TestProvider([definition with { ClientCode = "alpha" }]),
            new TestProvider([definition with { ClientCode = "beta" }])
        ]);

        Assert.Equal(2, catalog.GetByModule("users").Count);
        Assert.Single(catalog.GetByModule("users", "alpha"));
        Assert.Single(catalog.GetByModule("users", "beta"));
    }

    [Theory]
    [InlineData(PermissionFlags.None)]
    [InlineData((PermissionFlags)64)]
    public void RegistryRejectsEmptyOrUnknownPermissionBits(PermissionFlags flags)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = flags,
            DisplayName = "Invalid"
        }));
    }

    [Theory]
    [InlineData("/users/:id:extra")]
    [InlineData("/users/{user-id}")]
    [InlineData("/users/{id")]
    public void RegistryRejectsMalformedRouteTemplates(string route)
    {
        Assert.Throws<ArgumentException>(() => CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query,
            DisplayName = "Read users",
            FrontendRoutes = [route]
        }));
    }

    [Fact]
    public async Task AuthorizationRequiresCatalogDefinitionAndExactTenantScopedExplicitGrants()
    {
        var catalog = CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query | PermissionFlags.Modify,
            DisplayName = "Users"
        });
        var provider = new TestGrantProvider(
        [
            new PermissionGrant(7, "users", PermissionFlags.Query),
            new PermissionGrant(8, "users", PermissionFlags.Modify)
        ]);
        var service = new PermissionAuthorizationService(catalog, provider);

        Assert.True(await service.IsGrantedAsync(42, 7, "users", PermissionFlags.Query));
        Assert.False(await service.IsGrantedAsync(42, 7, "users", PermissionFlags.Modify));
        Assert.False(await service.IsGrantedAsync(42, 7, "users", PermissionFlags.Query | PermissionFlags.Modify));
        Assert.False(await service.IsGrantedAsync(42, 7, "missing", PermissionFlags.Query));
        Assert.Equal((42, (long?)7), (provider.LastSubjectId, provider.LastTenantId));
    }

    [Fact]
    public async Task AuthorizationRejectsInvalidIdentityTenantAndUndefinedRequirements()
    {
        var catalog = CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query,
            DisplayName = "Users"
        });
        var provider = new TestGrantProvider([new PermissionGrant(null, "users", PermissionFlags.Query)]);
        var service = new PermissionAuthorizationService(catalog, provider);

        Assert.False(await service.IsGrantedAsync(0, null, "users", PermissionFlags.Query));
        Assert.False(await service.IsGrantedAsync(42, 0, "users", PermissionFlags.Query));
        Assert.False(await service.IsGrantedAsync(42, null, "users", PermissionFlags.None));
        Assert.False(await service.IsGrantedAsync(42, null, "users", PermissionFlags.Delete));
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task AuthorizationPropagatesCancellationToGrantProvider()
    {
        var catalog = CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query,
            DisplayName = "Users"
        });
        var provider = new TestGrantProvider([]);
        var service = new PermissionAuthorizationService(catalog, provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await service.IsGrantedAsync(42, null, "users", PermissionFlags.Query, cancellation.Token);

        Assert.Equal(cancellation.Token, provider.LastCancellationToken);
    }

    [Fact]
    public async Task AuthorizationChecksExplicitCustomNamesAndExtensionBits()
    {
        var catalog = CreateCatalog(
            new PermissionDefinition
            {
                ModuleKey = "users",
                Flags = PermissionFlags.Custom,
                DisplayName = "Custom user permissions"
            },
            new PermissionDefinition
            {
                ModuleKey = "users",
                Flags = PermissionFlags.Custom,
                DisplayName = "Extended user permissions",
                IsExtPermission = true,
                ExtFlag = 4
            });
        var customPermissions = new List<string> { "export" };
        var provider = new TestGrantProvider(
        [
            new PermissionGrant(7, "users", PermissionFlags.None, extensionFlags: 4,
                customPermissions: customPermissions)
        ]);
        customPermissions.Add("delete");
        var service = new PermissionAuthorizationService(catalog, provider);

        Assert.True(await service.IsCustomPermissionGrantedAsync(42, 7, "users", "export"));
        Assert.False(await service.IsCustomPermissionGrantedAsync(42, 7, "users", "Export"));
        Assert.False(await service.IsCustomPermissionGrantedAsync(42, 7, "users", "delete"));
        Assert.True(await service.IsExtensionPermissionGrantedAsync(42, 7, "users", 4));
        Assert.False(await service.IsExtensionPermissionGrantedAsync(42, 7, "users", 8));
    }

    [Fact]
    public async Task AuthorizationCombinesRoleBitmasksWithBitwiseOr()
    {
        var catalog = CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query | PermissionFlags.Modify,
            DisplayName = "Users"
        });
        var provider = new TestGrantProvider(
        [
            new PermissionGrant(7, "users", PermissionFlags.Query),
            new PermissionGrant(7, "users", PermissionFlags.Modify)
        ]);
        var service = new PermissionAuthorizationService(catalog, provider);

        Assert.True(await service.IsGrantedAsync(42, 7, "users", PermissionFlags.Query));
        Assert.True(await service.IsGrantedAsync(42, 7, "users", PermissionFlags.Modify));
        Assert.True(await service.IsGrantedAsync(42, 7, "users", PermissionFlags.Query | PermissionFlags.Modify));
    }

    [Fact]
    public async Task AuthorizationCombinesCustomNamesAndExtensionBitsFromRoleGrants()
    {
        var catalog = CreateCatalog(
            new PermissionDefinition
            {
                ModuleKey = "users",
                Flags = PermissionFlags.Custom,
                DisplayName = "Custom user permissions"
            },
            new PermissionDefinition
            {
                ModuleKey = "users",
                Flags = PermissionFlags.Custom,
                DisplayName = "Extended user permissions",
                IsExtPermission = true,
                ExtFlag = 4 | 8
            });
        var provider = new TestGrantProvider(
        [
            new PermissionGrant(7, "users", PermissionFlags.None, extensionFlags: 4,
                customPermissions: ["export"]),
            new PermissionGrant(7, "users", PermissionFlags.None, extensionFlags: 8,
                customPermissions: ["archive"])
        ]);
        var service = new PermissionAuthorizationService(catalog, provider);

        Assert.True(await service.IsCustomPermissionGrantedAsync(42, 7, "users", "export"));
        Assert.True(await service.IsCustomPermissionGrantedAsync(42, 7, "users", "archive"));
        Assert.True(await service.IsExtensionPermissionGrantedAsync(42, 7, "users", 4));
        Assert.True(await service.IsExtensionPermissionGrantedAsync(42, 7, "users", 8));
        Assert.True(await service.IsExtensionPermissionGrantedAsync(42, 7, "users", 4 | 8));
    }

    [Fact]
    public void PermissionGrantRejectsEmptyGrants()
    {
        Assert.Throws<ArgumentException>(() => new PermissionGrant(
            7, "users", PermissionFlags.None));
    }

    [Fact]
    public async Task AuthorizationDoesNotApplyHostGrantToTenantOrTenantGrantToHost()
    {
        var catalog = CreateCatalog(new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query,
            DisplayName = "Users"
        });
        var provider = new TestGrantProvider(
        [
            new PermissionGrant(null, "users", PermissionFlags.Query),
            new PermissionGrant(7, "users", PermissionFlags.Query)
        ]);
        var service = new PermissionAuthorizationService(catalog, provider);

        Assert.True(await service.IsGrantedAsync(42, null, "users", PermissionFlags.Query));
        Assert.True(await service.IsGrantedAsync(42, 7, "users", PermissionFlags.Query));
        Assert.False(await service.IsGrantedAsync(42, 8, "users", PermissionFlags.Query));
    }

    private static PermissionDefinitionRegistry CreateCatalog(params PermissionDefinition[] definitions) =>
        new([new TestProvider(definitions)]);

    private sealed class TestProvider(IReadOnlyList<PermissionDefinition> definitions) : IPermissionDefinitionProvider
    {
        public IReadOnlyList<PermissionDefinition> GetDefinitions() => definitions;
    }

    private sealed class TestGrantProvider(IReadOnlyList<PermissionGrant> values) : IPermissionGrantProvider
    {
        public int CallCount { get; private set; }
        public long LastSubjectId { get; private set; }
        public long? LastTenantId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
            long subjectId,
            long? tenantId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastSubjectId = subjectId;
            LastTenantId = tenantId;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(values);
        }
    }
}