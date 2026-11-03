using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PubSoft.AspNetCore;
using PubSoft.Authorization;
using PubSoft.Cryptography;
using PubSoft.Domain;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LiftedWave2Tests
{
    // ── ClientSigner ──

    [Fact]
    public void ClientSignerSignAndVerifyRoundTrips()
    {
        var sig = ClientSigner.Sign("secret", "GET", "/api/x?a=1", "1700000000000", "n1", "abc");
        Assert.True(ClientSigner.Verify("secret", "GET", "/api/x?a=1", "1700000000000", "n1", "abc", sig));
        Assert.False(ClientSigner.Verify("secret", "GET", "/api/x?a=1", "1700000000000", "n2", "abc", sig));
        Assert.False(ClientSigner.Verify("wrong", "GET", "/api/x?a=1", "1700000000000", "n1", "abc", sig));
    }

    [Fact]
    public void ClientSignerBodyHashEmptyAndContent()
    {
        Assert.Equal("", ClientSigner.ComputeBodyHash(null));
        Assert.Equal("", ClientSigner.ComputeBodyHash(Array.Empty<byte>()));
        var h = ClientSigner.ComputeBodyHash(System.Text.Encoding.UTF8.GetBytes("hello"));
        Assert.Equal(64, h.Length);
    }

    [Fact]
    public void ClientSignerTimestampWindow()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        Assert.True(ClientSigner.IsTimestampValid(now));
        var old = (DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10)).ToUnixTimeMilliseconds().ToString();
        Assert.False(ClientSigner.IsTimestampValid(old));
        Assert.False(ClientSigner.IsTimestampValid("not-a-number"));
    }

    // ── HttpContextTenantContext ──

    private static HttpContextTenantContext Ctx(HttpContext http, Action<HttpContextTenantOptions>? cfg = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = http };
        var options = new HttpContextTenantOptions();
        cfg?.Invoke(options);
        return new HttpContextTenantContext(accessor, options);
    }

    [Fact]
    public void TenantContextReadsHeaderOnlyWhenEnabled()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Tenant-Id"] = "42";
        // default: ReadHeader false → header ignored
        Assert.Null(Ctx(http).TenantId);
        // opt-in: header read
        Assert.Equal(42, Ctx(http, o => o.ReadHeader = true).TenantId);
    }

    [Fact]
    public void TenantContextHeaderBeatsClaimWhenEnabled()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Tenant-Id"] = "42";
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", "7") }, "test"));
        Assert.Equal(42, Ctx(http, o => o.ReadHeader = true).TenantId);
    }

    [Fact]
    public void TenantContextAmbientOverrideWins()
    {
        var http = new DefaultHttpContext();
        var ctx = Ctx(http);
        using var scope = ctx.Change(99);
        Assert.Equal(99, ctx.TenantId);
    }

    [Fact]
    public void AddPubSoftTenantContextRegistersScoped()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddPubSoftTenantContext();
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetRequiredService<ITenantContext>());
    }

    // ── PermissionModuleDiscovery ──

    private sealed class FakeActions(params ControllerActionDescriptor[] actions) : IActionDescriptorCollectionProvider
    {
        public ActionDescriptorCollection ActionDescriptors { get; } = new(actions, 1);
    }

    private static ControllerActionDescriptor Action(string route, params object[] metadata)
        => new()
        {
            AttributeRouteInfo = new AttributeRouteInfo { Template = route },
            EndpointMetadata = metadata.ToList(),
        };

    private sealed class FakeProvider(string[] keys) : IPermissionModuleProvider
    {
        public IReadOnlyCollection<string> All => keys;
        public string GetDisplayName(string moduleKey) => $"display-{moduleKey}";
    }

    [Fact]
    public void DiscoverySkipsAnonymousAndGroupsByKey()
    {
        var actions = new FakeActions(
            Action("api/x/list", new PermissionModuleAttributeStub("mod_a")),
            Action("api/anon", new AllowAnonymousAttribute(), new PermissionModuleAttributeStub("mod_a")),
            Action("api/y/item", new PermissionModuleAttributeStub("mod_b")));
        var d = new PermissionModuleDiscovery(actions, new[] { new FakeProvider(new[] { "mod_a", "mod_b", "mod_unclaimed" }) },
            Options.Create(new PermissionModuleDiscoveryOptions
            {
                ModuleKeyResolver = a => a.EndpointMetadata.OfType<PermissionModuleAttributeStub>().FirstOrDefault()?.Key,
            }));
        var modules = d.Scan();
        Assert.Equal(3, modules.Count);
        var a = modules.Single(m => m.ModuleKey == "mod_a");
        Assert.Single(a.ApiPaths);          // anonymous action excluded from paths
        var unclaimed = modules.Single(m => m.ModuleKey == "mod_unclaimed");
        Assert.Empty(unclaimed.ApiPaths);   // provider-declared key with no actions still listed
    }

    [Fact]
    public void DiscoverySortsByProviderOrderThenKey()
    {
        var actions = new FakeActions(Action("api/z/a", new PermissionModuleAttributeStub("mod_z")));
        var d = new PermissionModuleDiscovery(actions, new[] { new FakeProvider(new[] { "mod_b", "mod_a", "mod_z" }) },
            Options.Create(new PermissionModuleDiscoveryOptions
            {
                ModuleKeyResolver = a => a.EndpointMetadata.OfType<PermissionModuleAttributeStub>().FirstOrDefault()?.Key,
            }));
        var keys = d.Scan().Select(m => m.ModuleKey).ToList();
        Assert.Equal(new[] { "mod_b", "mod_a", "mod_z" }, keys);
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class PermissionModuleAttributeStub(string key) : Attribute { public string Key => key; }

    // ── JwksKeyProvider ──

    private sealed class StubHttp(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(json) });
    }

    [Fact]
    public async Task JwksKeyProviderResolvesKnownKidAndMissesUnknown()
    {
        var rsa = System.Security.Cryptography.RSA.Create();
        var key = new RsaSecurityKey(rsa) { KeyId = "k1" };
        var jwks = new JsonWebKeySet();
        jwks.Keys.Add(JsonWebKeyConverter.ConvertFromRSASecurityKey(key));
        var json = System.Text.Json.JsonSerializer.Serialize(jwks);

        var provider = new PubSoft.Authentication.Jwt.AspNetCore.JwksKeyProvider(
            Options.Create(new PubSoft.Authentication.Jwt.AspNetCore.JwksOptions { JwksUri = "https://id.test/jwks.json" }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<PubSoft.Authentication.Jwt.AspNetCore.JwksKeyProvider>.Instance,
            new HttpClient(new StubHttp(json)));

        Assert.NotNull(await provider.ResolveSigningKeyAsync("k1"));
        Assert.Null(await provider.ResolveSigningKeyAsync("missing"));
    }

    // ── ServiceDefaults / GatewayRateLimiting ──

    [Fact]
    public void ServiceDefaultsRegistersExceptionAndHealthPieces()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder().Build();
        services.AddPubSoftServiceDefaults(config);
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<HealthCheckAggregator>());
        Assert.NotNull(sp.GetService<Microsoft.AspNetCore.Diagnostics.IExceptionHandler>());
    }

    [Fact]
    public void GatewayRateLimitingDisabledSkipsForwardedHeadersRewrite()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:RateLimit:Enabled"] = "false" })
            .Build();
        services.AddPubSoftGatewayRateLimiting(config);
        using var sp = services.BuildServiceProvider();
        var fh = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>().Value;
        // disabled → untouched: ForwardedHeaders should not be rewritten
        Assert.NotEqual(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto, fh.ForwardedHeaders);
    }
}
