using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PubSoft.Authentication.Jwt;
using PubSoft.EntityFrameworkCore;
using PubSoft.Monitoring;
using PubSoft.MultiTenancy;
using PubSoft.Tokens;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MachineTokenAndEfHealthTests
{
    [Fact]
    public async Task MachineToken_IssuesSignedJwt_AndCachesUntilRefreshBefore()
    {
        var provider = new MachineTokenProvider(Options());
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

        var first = await provider.GetTokenAsync("svc");
        var second = await provider.GetTokenAsync("svc");

        Assert.Same(first, second);
        var jwt = claims.ReadJwtToken(first);
        Assert.Equal("issuer", jwt.Issuer);
        Assert.Equal("api", jwt.Audiences.Single());
        Assert.Equal("Machine", jwt.Claims.Single(c => c.Type == "caller_type").Value);

        await provider.InvalidateAsync("svc");
        var third = await provider.GetTokenAsync("svc");
        Assert.NotSame(first, third);
    }

    [Fact]
    public async Task MachineToken_UnknownCredential_YieldsClaimlessToken()
    {
        var provider = new MachineTokenProvider(Options());
        var token = await provider.GetTokenAsync("unknown");
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "caller_type");
    }

    [Fact]
    public async Task EfCoreHealthCheck_HealthyWhenConnectable()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ProbeDb>(o => o.UseSqlite("Data Source=:memory:"));
        services.AddPubSoftEfCoreHealthCheck<ProbeDb>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IEnumerable<IAppHealthCheck>>().Single().CheckAsync();

        Assert.Equal(AppHealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void SnowflakeConventions_MarksLongKeysValueGeneratedNever()
    {
        var builder = new ModelBuilder();
        builder.Entity<Keyed>().HasKey(e => e.Id);
        builder.Entity<Keyed>().Property(e => e.Code);

        builder.ApplySnowflakeIdConventions();
        var model = builder.FinalizeModel();

        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never,
            model.FindEntityType(typeof(Keyed))!.FindPrimaryKey()!.Properties[0].ValueGenerated);
        Assert.False(model.FindEntityType(typeof(Keyed))!.FindProperty(nameof(Keyed.Code))!.IsPrimaryKey());
    }

    private static IOptions<MachineTokenOptions> Options() => Microsoft.Extensions.Options.Options.Create(new MachineTokenOptions
    {
        Issuer = "issuer",
        Audience = "api",
        SigningKey = "test-signing-key-0123456789abcdef0123",
        Credentials = { ["svc"] = new MachineTokenCredential { Claims = { ["caller_type"] = "Machine" } } }
    });

    private sealed class Keyed { public long Id { get; set; } public long Code { get; set; } }
    private sealed class ProbeDb(DbContextOptions<ProbeDb> o) : DbContext(o);
}
