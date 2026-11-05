using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PubSoft.Authentication.Jwt.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DualJwtWaveTests
{
    private const string Issuer = "https://id.test";

    [Fact]
    public void AuthServerSchemeValidatesConfiguredAudience()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = "api-aud",
            ["Jwt:SecretKey"] = new string('k', 48),
            ["AuthServer:Issuer"] = Issuer,
            ["AuthServer:JwksUri"] = "https://id.test/jwks.json",
            ["AuthServer:Audience"] = "expected-aud",
        });

        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(DualJwtAuthenticationExtensions.AuthServerScheme);

        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.Equal("expected-aud", options.TokenValidationParameters.ValidAudience);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.Equal(Issuer, options.TokenValidationParameters.ValidIssuer);
        Assert.NotNull(options.TokenValidationParameters.IssuerSigningKeyResolver);
    }

    [Fact]
    public void AuthServerSchemeSkipsAudienceWhenUnconfigured()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = new string('k', 48),
            ["AuthServer:Issuer"] = Issuer,
            ["AuthServer:JwksUri"] = "https://id.test/jwks.json",
        });

        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(DualJwtAuthenticationExtensions.AuthServerScheme);

        Assert.False(options.TokenValidationParameters.ValidateAudience);
    }

    [Fact]
    public void SchemesPinTheirExpectedAlgorithms()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = new string('k', 48),
            ["AuthServer:Issuer"] = Issuer,
            ["AuthServer:JwksUri"] = "https://id.test/jwks.json",
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        var authServer = monitor.Get(DualJwtAuthenticationExtensions.AuthServerScheme);
        Assert.Equal(new[] { "RS256" }, authServer.TokenValidationParameters.ValidAlgorithms);

        var bearer = monitor.Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(new[] { "HS256" }, bearer.TokenValidationParameters.ValidAlgorithms);
        Assert.True(bearer.TokenValidationParameters.ValidateAudience);
    }

    [Fact]
    public void KeyProviderResolvesThroughHostContainer()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = new string('k', 48),
            ["AuthServer:Issuer"] = Issuer,
            ["AuthServer:JwksUri"] = "https://id.test/jwks.json",
        });

        // Options configuration pulls JwksKeyProvider from the real container;
        // a second BuildServiceProvider inside registration would have resolved a
        // different singleton. Resolving the options proves the DI wiring works.
        var monitor = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        _ = monitor.Get(DualJwtAuthenticationExtensions.AuthServerScheme);

        var first = provider.GetRequiredService<JwksKeyProvider>();
        var second = provider.GetRequiredService<JwksKeyProvider>();
        Assert.Same(first, second);
    }

    [Theory]
    [InlineData("http://id.test/jwks.json")]
    [InlineData("not-a-uri")]
    public void NonHttpsJwksUriFailsStartup(string jwksUri)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AuthServer:Issuer"] = Issuer,
            ["AuthServer:JwksUri"] = jwksUri,
        });
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddPubSoftDualJwtAuthentication(config));
    }

    [Fact]
    public void ShortSecretKeyFailsStartup()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = new string('k', 16),
        });
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(
            () => services.AddPubSoftDualJwtAuthentication(config));
    }

    [Fact]
    public void LoopbackHttpJwksUriIsAllowedForDevelopment()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = new string('k', 48),
            ["AuthServer:Issuer"] = "http://localhost:8080",
            ["AuthServer:JwksUri"] = "http://localhost:8080/jwks.json",
        });
        var services = new ServiceCollection();

        services.AddPubSoftDualJwtAuthentication(config);

        Assert.Contains(services, d => d.ServiceType == typeof(JwksKeyProvider));
    }

    [Fact]
    public void NonPositiveCacheMinutesFailsStartup()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AuthServer:Issuer"] = Issuer,
            ["AuthServer:JwksUri"] = "https://id.test/jwks.json",
            ["AuthServer:CacheMinutes"] = "0",
        });
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddPubSoftDualJwtAuthentication(config));
    }

    [Fact]
    public async Task RemovedKeyIsNotAcceptedAfterJwksRefresh()
    {
        var jwksV1 = JwksJson("key1", "key2");
        var jwksV2 = JwksJson("key2");
        var handler = new SwapHandler(jwksV1);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = NewKeyProvider(handler, cache);

        var key1 = await provider.ResolveSigningKeyAsync("key1");
        Assert.NotNull(key1);

        // JWKS drops key1 — once the cached set expires, the old key ID must stop resolving.
        handler.Json = jwksV2;
        cache.Remove("jwks_keys");

        Assert.Null(await provider.ResolveSigningKeyAsync("key1"));
        Assert.NotNull(await provider.ResolveSigningKeyAsync("key2"));
    }

    [Fact]
    public async Task EmptyJwksSetIsCachedForTheTtl()
    {
        var handler = new SwapHandler("{\"keys\":[]}");
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = NewKeyProvider(handler, cache);

        Assert.Null(await provider.ResolveSigningKeyAsync("key1"));

        // Within the TTL an empty set is served from cache — no refetch storm, and a
        // key appearing in JWKS is honored only after the cached set expires.
        var fetchCount = handler.FetchCount;
        Assert.Null(await provider.ResolveSigningKeyAsync("key1"));
        Assert.Equal(fetchCount, handler.FetchCount);

        handler.Json = JwksJson("key1");
        Assert.Null(await provider.ResolveSigningKeyAsync("key1"));
        cache.Remove("jwks_keys");
        Assert.NotNull(await provider.ResolveSigningKeyAsync("key1"));
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddPubSoftDualJwtAuthentication(BuildConfig(values));
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static JwksKeyProvider NewKeyProvider(HttpMessageHandler handler, IMemoryCache cache)
        => new(
            Options.Create(new JwksOptions { Issuer = Issuer, JwksUri = "https://id.test/jwks.json" }),
            cache,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JwksKeyProvider>.Instance,
            new HttpClient(handler));

    private static string JwksJson(params string[] keyIds)
    {
        var keys = keyIds.Select(kid =>
        {
            using var rsa = RSA.Create(2048);
            var p = rsa.ExportParameters(includePrivateParameters: false);
            var n = Base64UrlEncoder.Encode(p.Modulus);
            var e = Base64UrlEncoder.Encode(p.Exponent);
            return $"{{\"kty\":\"RSA\",\"use\":\"sig\",\"kid\":\"{kid}\",\"n\":\"{n}\",\"e\":\"{e}\"}}";
        });
        return $"{{\"keys\":[{string.Join(',', keys)}]}}";
    }

    private sealed class SwapHandler(string json) : HttpMessageHandler
    {
        public string Json = json;
        public int FetchCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            FetchCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
