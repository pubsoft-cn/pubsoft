using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PubSoft.EntityFrameworkCore;
using PubSoft.Monitoring;
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
    public async Task AddPubSoftMachineTokens_BindsIssuerAudienceFromConfig()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Hwd",
                ["Jwt:Audience"] = "Hwd",
                ["Jwt:SecretKey"] = System.Convert.ToBase64String(new byte[48]),
                ["Jwt:Credentials:svc:Claims:caller_type"] = "Machine"
            }).Build();
        var services = new ServiceCollection();
        services.AddPubSoftMachineTokens(config);
        using var provider = services.BuildServiceProvider();

        var tokenProvider = provider.GetRequiredService<ITokenProvider>();
        Assert.IsType<MachineTokenProvider>(tokenProvider);
        var token = await tokenProvider.GetTokenAsync("svc");
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("Hwd", jwt.Issuer); // ordinal match with validator-side ValidIssuer
        Assert.Equal("Hwd", jwt.Audiences.Single());
        Assert.Equal("Machine", jwt.Claims.Single(c => c.Type == "caller_type").Value);
    }

    [Fact]
    public void AddPubSoftMachineTokens_RejectsShortSigningKey()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "issuer",
                ["Jwt:Audience"] = "api",
                ["Jwt:SigningKey"] = "too-short" // SigningKey alias must still bind — a short key proves it was read
            }).Build();
        var services = new ServiceCollection();
        services.AddPubSoftMachineTokens(config);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(
            () => provider.GetRequiredService<ITokenProvider>());
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
        SigningKey = System.Convert.ToBase64String(new byte[48]),
        Credentials = { ["svc"] = new MachineTokenCredential { Claims = { ["caller_type"] = "Machine" } } }
    });

    private sealed class Keyed { public long Id { get; set; } public long Code { get; set; } }
    private sealed class ProbeDb(DbContextOptions<ProbeDb> o) : DbContext(o);
    [Fact]
    public void MachineToken_ResolvesViaKeyedService_WhenCompetingProviderRegistered()
    {
        var services = new ServiceCollection();
        // a competing default provider registered first — ordering must not matter
        services.TryAddSingleton<ITokenProvider>(new FakeProvider());
        services.AddPubSoftMachineTokens(o =>
        {
            o.Issuer = "issuer"; o.Audience = "api"; o.KeyId = "k1";
            o.SigningKey = new string('k', 48);
        });
        var sp = services.BuildServiceProvider();

        Assert.IsType<FakeProvider>(sp.GetRequiredService<ITokenProvider>());
        var keyed = sp.GetKeyedService<ITokenProvider>(MachineTokenProvider.ServiceKey);
        Assert.IsType<MachineTokenProvider>(keyed);
        Assert.Same(keyed, sp.GetRequiredService<MachineTokenProvider>());
    }

    private sealed class FakeProvider : ITokenProvider
    {
        public Task<string?> GetTokenAsync(string credentialKey, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("fake");
        public Task InvalidateAsync(string credentialKey, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

}
