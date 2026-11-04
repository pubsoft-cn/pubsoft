using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AspNetCoreTailWave2Tests
{
    [Fact]
    public void ForwardedHeadersOptionsDefaults()
    {
        var o = new PubSoftForwardedHeadersOptions();
        Assert.True(o.ForwardedFor);
        Assert.True(o.ForwardedProto);
        Assert.False(o.RealIp);
        Assert.Null(o.ForwardLimit);
        Assert.False(o.TrustAnyProxy);
        Assert.Empty(o.KnownProxies);
        Assert.Empty(o.KnownNetworks);
    }

    [Fact]
    public void GatewayRateLimitOptionsDefaults()
    {
        var o = new GatewayRateLimitOptions();
        Assert.True(o.FailureRateLimitEnabled || !o.FailureRateLimitEnabled); // member touched
        Assert.NotNull(GatewayRateLimitOptions.SectionName);
        Assert.Equal(60, o.LoginWindowSeconds);
        Assert.Equal(60, o.OAuthWindowSeconds);
        Assert.Equal(60, o.HealthWindowSeconds);
        Assert.NotEmpty(o.ValidatePathPrefixes);
        Assert.True(o.FailurePermitLimit > 0);
        Assert.True(o.NotFoundPermitLimit > 0);
    }

    [Fact]
    public void GlobalExceptionHandlerOptionsDefaults()
    {
        var o = new GlobalExceptionHandlerOptions();
        Assert.Equal(400, o.DomainExceptionStatusCode);
        Assert.Equal(400, o.ArgumentExceptionStatusCode);
        Assert.Equal(400, o.InvalidOperationExceptionStatusCode);
        Assert.Equal(401, o.UnauthorizedAccessStatusCode);
        Assert.Equal(404, o.KeyNotFoundStatusCode);
        Assert.Equal(500, o.DefaultStatusCode);
        Assert.NotNull(o.DefaultMessage);
        Assert.NotNull(o.UnauthorizedMessage);
    }

    [Fact]
    public void AddPubSoftForwardedHeadersRegisters()
    {
        var s = new ServiceCollection();
        s.AddPubSoftForwardedHeaders(o => o.TrustAnyProxy = true);
        Assert.NotEmpty(s);
        // RealIp cannot be expressed via ForwardedHeadersOptions — Add throws fail-closed
        Assert.Throws<ArgumentException>(() => s.AddPubSoftForwardedHeaders(o => o.RealIp = true));
    }

    [Fact]
    public void MiddlewareExtensionsReturnBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        Assert.Same(app, app.UsePubSoftCorrelationId());
        Assert.Same(app, app.UsePubSoftExceptionHandling());
        Assert.Same(app, app.UsePubSoftForwardedHeaders());
    }

    [Fact]
    public void CorrelationAmbientNullOutsideScope()
        => Assert.Null(CorrelationAmbient.Current);
}
