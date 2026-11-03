using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AspNetCoreWave4Tests
{
    [Fact]
    public void GatewayRateLimitOptionsDefaults()
    {
        var o = new GatewayRateLimitOptions();
        Assert.True(o.Enabled);
        Assert.Equal(200, o.PermitLimit);
        Assert.Equal(60, o.WindowSeconds);
        Assert.True(o.PerEndpoint);
        Assert.Equal(20, o.LoginPermitLimit);
        Assert.Equal(2000, o.OAuthPermitLimit);
        Assert.Equal(30, o.OAuthFailureLimit);
        Assert.Equal(10, o.ValidateAnonymousPermitLimit);
        Assert.Equal(50, o.ValidateFailureLimit);
        Assert.Equal(60, o.HealthPermitLimit);
        Assert.Equal("/health", o.HealthPathPrefixes);
        Assert.True(o.NotFoundRateLimitEnabled);
    }

    [Fact]
    public void GatewayRateLimitingEnabledRewritesForwardedHeaders()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:RateLimit:Enabled"] = "true" })
            .Build();
        services.AddPubSoftGatewayRateLimiting(config);
        using var sp = services.BuildServiceProvider();
        var fh = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>().Value;
        Assert.Equal(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto, fh.ForwardedHeaders);
        Assert.Empty(fh.KnownIPNetworks);
        Assert.Empty(fh.KnownProxies);
        Assert.Null(fh.ForwardLimit);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task CorrelationPropagationWithoutAmbientAddsNoHeaders()
    {
        // boundary: no ambient correlation → handler must not stamp traceparent/correlation headers
        var inner = new CaptureHandler();
        var handler = new CorrelationPropagationHandler { InnerHandler = inner };
        var client = new HttpClient(handler);
        await client.GetAsync("https://x.test/api");
        Assert.False(inner.Last!.Headers.Contains("traceparent"));
        Assert.False(inner.Last.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public void SurfaceTypesExist()
    {
        Assert.NotNull(typeof(DevelopmentOnlyAttribute));
        Assert.NotNull(typeof(DevelopmentOnlyControllerConventionExtensions));
        Assert.NotNull(typeof(ExceptionHandlingMiddlewareExtensions));
        Assert.NotNull(typeof(GatewayRateLimitingExtensions));
    }
}
