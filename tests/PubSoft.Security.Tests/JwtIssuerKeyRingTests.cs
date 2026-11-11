using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PubSoft.Authentication.Jwt;
using PubSoft.Authentication.Jwt.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class JwtIssuerKeyRingTests
{
    [Fact]
    public void RingPublishesEveryKeyAndSignsWithTheActiveOne()
    {
        using var ring = new ConfigJwtIssuerKeyRing(new JwtIssuerKeyRingOptions
        {
            ActiveKeyId = "key-2",
            Keys = new Dictionary<string, string>
            {
                ["key-1"] = NewPrivateKeyPem(),
                ["key-2"] = NewPrivateKeyPem()
            }
        });

        Assert.Equal("key-2", ring.ActiveKey.KeyId);
        Assert.Equal(2, ring.PublishedKeys.Count);

        var token = JwtSigning.CreateAccessToken(
            ring.ActiveKey, "issuer", "api",
            new Dictionary<string, string> { [JwtRegisteredClaimNames.Sub] = "user-1" },
            TimeSpan.FromMinutes(5));
        Assert.Equal("key-2", new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid);

        // Tokens signed by any published key (e.g. a retiring one) still validate by kid.
        var claims = JwtSigning.ValidateAccessToken(token, "issuer", "api", ring.PublishedKeys);
        Assert.Equal("user-1", claims[JwtRegisteredClaimNames.Sub]);

        var jwks = JwtSigning.ExportJwks(ring.PublishedKeys);
        Assert.Equal(["key-1", "key-2"], jwks.Keys.Select(key => key.Kid).Order().ToArray());
    }

    [Fact]
    public void RingRejectsMisconfigurationFailClosed()
    {
        var validKeys = new Dictionary<string, string> { ["key-1"] = NewPrivateKeyPem() };

        Assert.Throws<ArgumentException>(() => new ConfigJwtIssuerKeyRing(new JwtIssuerKeyRingOptions
        {
            ActiveKeyId = "missing",
            Keys = validKeys
        }));
        Assert.Throws<ArgumentException>(() => new ConfigJwtIssuerKeyRing(new JwtIssuerKeyRingOptions
        {
            ActiveKeyId = "key-1",
            Keys = new Dictionary<string, string>()
        }));
        Assert.Throws<ArgumentException>(() => new ConfigJwtIssuerKeyRing(new JwtIssuerKeyRingOptions
        {
            ActiveKeyId = "key-1",
            Keys = new Dictionary<string, string> { ["key-1"] = "not-a-pem" }
        }));
        Assert.Throws<ArgumentException>(() => new ConfigJwtIssuerKeyRing(new JwtIssuerKeyRingOptions
        {
            ActiveKeyId = "key-1",
            Keys = new Dictionary<string, string> { ["key-1"] = NewPrivateKeyPem(keySize: 1024) }
        }));
    }

    [Fact]
    public void RegistrationBindsSectionAndKeepsFirstRing()
    {
        var firstPem = NewPrivateKeyPem();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:IssuerKeys:ActiveKeyId"] = "active",
                ["Jwt:IssuerKeys:Keys:active"] = firstPem,
                ["Jwt:IssuerKeys:Keys:retiring"] = NewPrivateKeyPem()
            }).Build();

        var services = new ServiceCollection();
        services.AddPubSoftJwtIssuerKeyRing(configuration);
        // Second registration is a no-op for consumers and must not leak the discarded ring.
        services.AddPubSoftJwtIssuerKeyRing(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:IssuerKeys:ActiveKeyId"] = "other",
                ["Jwt:IssuerKeys:Keys:other"] = NewPrivateKeyPem()
            }).Build());

        using var provider = services.BuildServiceProvider();
        var ring = Assert.Single(provider.GetServices<IJwtIssuerKeyRing>());
        Assert.Equal("active", ring.ActiveKey.KeyId);
        Assert.Equal(2, ring.PublishedKeys.Count);
    }

    [Fact]
    public void RegistrationFailsClosedOnMissingSection()
    {
        var empty = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddPubSoftJwtIssuerKeyRing(empty));
    }

    [Fact]
    public void JwksEndpointFailsClosedWithoutRing()
    {
        var builder = WebApplication.CreateSlimBuilder();
        using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.MapPubSoftJwks());
    }

    [Fact]
    public async Task JwksEndpointServesOnlyPublicParameters()
    {
        using var ring = new ConfigJwtIssuerKeyRing(new JwtIssuerKeyRingOptions
        {
            ActiveKeyId = "key-1",
            Keys = new Dictionary<string, string> { ["key-1"] = NewPrivateKeyPem() }
        });

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IJwtIssuerKeyRing>(ring);
        await using var app = builder.Build();
        app.MapPubSoftJwks();

        var routeBuilder = (IEndpointRouteBuilder)app;
        var endpoint = Assert.IsType<RouteEndpoint>(Assert.Single(
            routeBuilder.DataSources.SelectMany(dataSource => dataSource.Endpoints)));
        Assert.Equal("/.well-known/jwks.json", endpoint.RoutePattern.RawText);

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);

        Assert.Equal("application/json", context.Response.ContentType);
        Assert.Equal("public, max-age=60", context.Response.Headers.CacheControl.ToString());

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var key = Assert.Single(document.RootElement.GetProperty("keys").EnumerateArray());
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("key-1", key.GetProperty("kid").GetString());
        Assert.False(string.IsNullOrWhiteSpace(key.GetProperty("n").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(key.GetProperty("e").GetString()));
        // No private JWK parameters may leak.
        var raw = document.RootElement.GetRawText();
        foreach (var parameter in new[] { "\"d\"", "\"p\"", "\"q\"", "\"dp\"", "\"dq\"", "\"qi\"", "\"oth\"" })
            Assert.DoesNotContain(parameter, raw);
    }

    private static string NewPrivateKeyPem(int keySize = 2048)
    {
        using var rsa = RSA.Create(keySize);
        return rsa.ExportPkcs8PrivateKeyPem();
    }
}
