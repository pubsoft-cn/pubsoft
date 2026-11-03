using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PubSoft.Authentication.Jwt.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class JwtAspNetCoreWaveTests
{
    [Fact]
    public void PubSoftJwtBearerOptionsDefaults()
    {
        var o = new PubSoftJwtBearerOptions();
        Assert.Equal(string.Empty, o.Issuer);
        Assert.True(o.RequireHttpsMetadata);
        Assert.Equal(TimeSpan.FromSeconds(15), o.BackchannelTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), o.ClockSkew);
        Assert.Equal("sub", o.NameClaimType);
        Assert.Equal("role", o.RoleClaimType);
    }

    [Fact]
    public void JwksOptionsBindsAuthServerSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthServer:Issuer"] = "https://id.test",
                ["AuthServer:JwksUri"] = "https://id.test/jwks.json",
                ["AuthServer:CacheMinutes"] = "30",
            }).Build();
        var o = config.GetSection(JwksOptions.SectionName).Get<JwksOptions>()!;
        Assert.Equal("https://id.test", o.Issuer);
        Assert.Equal("https://id.test/jwks.json", o.JwksUri);
        Assert.Equal(30, o.CacheMinutes);
        Assert.Equal("AuthServer", JwksOptions.SectionName);
    }

    [Fact]
    public void JwksOptionsDefaults()
    {
        var o = new JwksOptions();
        Assert.Equal(15, o.CacheMinutes);
        Assert.Equal(string.Empty, o.Issuer);
    }

    [Fact]
    public void SurfaceTypesExist()
    {
        Assert.NotNull(typeof(DualJwtAuthenticationExtensions));
        Assert.NotNull(typeof(JwtBearerAuthenticationExtensions));
        Assert.NotNull(typeof(JwksKeyProvider));
    }
}
