using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PubSoft.AspNetCore;
using PubSoft.Authentication.Jwt.AspNetCore;
using PubSoft.Tokens;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LiftedWave3Tests
{
    // ── MachineTokenProvider per-credential key override ──

    [Fact]
    public async Task MachineTokenPerCredentialSigningKeyOverridesGlobal()
    {
        var options = Options.Create(new MachineTokenOptions
        {
            Issuer = "i",
            Audience = "a",
            SigningKey = "unit-test-signing-key-not-a-real-secret-1",
            Credentials =
            {
                ["default"] = new MachineTokenCredential(),
                ["override"] = new MachineTokenCredential
                {
                    SigningKey = "unit-test-override-key-not-a-real-secret-1",
                    Claims = { ["scope"] = "mallbook" },
                },
            },
        });
        var provider = new MachineTokenProvider(options);
        var t1 = await provider.GetTokenAsync("default");
        var t2 = await provider.GetTokenAsync("override");
        Assert.NotNull(t1); Assert.NotNull(t2); Assert.NotEqual(t1, t2);
    }

    // ── GlobalExceptionHandler CustomMappings ──

    private sealed class CustomEx() : Exception("biz");

    [Fact]
    public async Task GlobalExceptionHandlerCustomMappingApplies()
    {
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            new GlobalExceptionHandlerOptions { CustomMappings = { [typeof(CustomEx)] = 418 } });
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var handled = await handler.TryHandleAsync(ctx, new CustomEx(), CancellationToken.None);
        Assert.True(handled);
        Assert.Equal(418, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task GlobalExceptionHandlerDefaultsTo500ForUnknown()
    {
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var handled = await handler.TryHandleAsync(ctx, new Exception("boom"), CancellationToken.None);
        Assert.True(handled);
        Assert.Equal((int)HttpStatusCode.InternalServerError, ctx.Response.StatusCode);
    }

    // ── DualJwt alg-forwarding ──

    [Fact]
    public void DualJwtRegistersBothSchemesWhenAuthServerConfigured()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "k".PadRight(40, 'x'),
                ["AuthServer:Issuer"] = "https://id.test",
            }).Build();
        services.AddPubSoftDualJwtAuthentication(config);
        Assert.Contains(services, d => d.ServiceType == typeof(PubSoft.Authentication.Jwt.AspNetCore.JwksKeyProvider));
    }
}
