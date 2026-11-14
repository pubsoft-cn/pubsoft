using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PubSoft.Authentication.Jwt;
using PubSoft.Authentication.Jwt.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CidClientKeyWaveTests
{
    private sealed class FakeClientKeys : IClientSigningKeyProvider
    {
        private readonly SymmetricSigningKeyMaterial _key = JwtSigning.ImportSymmetricKey("k-c1", new string('c', 48));
        public ValueTask<SymmetricSigningKeyMaterial?> GetSigningKeyAsync(string clientId, CancellationToken ct = default)
            => new(clientId == "client-1" ? _key : null);
    }

    private static ServiceProvider BuildProvider(bool withClientKeys)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        if (withClientKeys)
            services.AddSingleton<IClientSigningKeyProvider>(new FakeClientKeys());
        services.AddPubSoftDualJwtAuthentication(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "iss",
                ["Jwt:Audience"] = "aud",
                ["Jwt:SecretKey"] = new string('k', 48)
            }).Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void CidTokenResolvesPerClientKey()
    {
        using var sp = BuildProvider(withClientKeys: true);
        var tvp = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        Assert.Null(tvp.IssuerSigningKey); // resolver path replaces the shared-key slot
        Assert.NotNull(tvp.IssuerSigningKeyResolver);

        var cidToken = new JwtSecurityToken(claims: [new Claim("cid", "client-1")]);
        var clientKey = Assert.Single(tvp.IssuerSigningKeyResolver!("", cidToken, null, tvp).ToArray());
        Assert.Equal("k-c1", clientKey.KeyId);

        // JsonWebToken is what the bearer handler actually hands the resolver at runtime
        var jsonWebToken = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(
            "eyJhbGciOiJIUzI1NiIsImtpZCI6ImstYzEifQ.eyJjaWQiOiJjbGllbnQtMSJ9.sig");
        var jwtKey = Assert.Single(tvp.IssuerSigningKeyResolver!("", jsonWebToken, null, tvp).ToArray());
        Assert.Equal("k-c1", jwtKey.KeyId);

        var unknownCid = new JwtSecurityToken(claims: [new Claim("cid", "nobody")]);
        Assert.Empty(tvp.IssuerSigningKeyResolver!("", unknownCid, null, tvp));

        var noCid = new JwtSecurityToken();
        Assert.Single(tvp.IssuerSigningKeyResolver!("", noCid, null, tvp)); // shared-key fallback
    }

    [Fact]
    public void WithoutClientKeyProviderSharedKeyRemains()
    {
        using var sp = BuildProvider(withClientKeys: false);
        var tvp = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        Assert.NotNull(tvp.IssuerSigningKey);
        Assert.Null(tvp.IssuerSigningKeyResolver);
    }
}
