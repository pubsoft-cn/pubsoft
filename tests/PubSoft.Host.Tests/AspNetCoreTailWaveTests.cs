using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AspNetCoreTailWaveTests
{
    [Fact]
    public void CorrelationIdOptionsDefaults()
    {
        var o = new CorrelationIdOptions();
        Assert.Equal("X-Correlation-Id", o.ResponseHeaderName);
        Assert.Equal(["X-Correlation-Id", "X-Request-Id"], o.InboundHeaderNames);
        Assert.True(o.EchoResponseHeader);
        Assert.True(o.Enabled);
    }

    [Fact]
    public void CorrelationAmbientNewSpanIdFormat()
    {
        var id = CorrelationAmbient.NewSpanId();
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public void CorrelationPropagationHandlerHeaderDefault()
    {
        var h = new CorrelationPropagationHandler();
        Assert.Equal("X-Correlation-Id", h.CorrelationHeaderName);
    }

    [Fact]
    public void PubSoftForwardedHeadersOptionsDefaults()
    {
        var o = new PubSoftForwardedHeadersOptions();
        Assert.NotNull(o.ForwardedFor);
        Assert.NotNull(o.ForwardedProto);
    }

    [Fact]
    public void GatewayRateLimitOptionsAdditionalFields()
    {
        var o = new GatewayRateLimitOptions();
        Assert.True(o.QueueLimit >= 0);
        Assert.NotNull(o.LoginPathPrefixes);
        Assert.NotNull(o.OAuthPathPrefixes);
    }
}
