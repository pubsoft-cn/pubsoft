using System.Net;
using Microsoft.AspNetCore.Http;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ProxyDeploymentDiagnosticsTests
{
    [Fact]
    public void DetectsUnprocessedHeaders_BehindProxy()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        Assert.True(ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders(context));
    }

    [Fact]
    public void DetectsUnprocessedHeaders_LoopbackProxy()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.23, 10.0.0.1";

        Assert.True(ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders(context));
    }

    [Fact]
    public void DetectsUnprocessedHeaders_ViaXRealIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
        context.Request.Headers["X-Real-IP"] = "203.0.113.9";

        Assert.True(ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders(context));
    }

    [Fact]
    public void IgnoresProcessedHeaders_PublicRemoteIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        Assert.False(ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders(context));
    }

    [Fact]
    public void IgnoresDirectConnections_NoHeaders()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.False(ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders(context));
    }

    [Fact]
    public void IgnoresInternalTraffic_PrivateAdvertised()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.3";

        Assert.False(ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders(context));
    }
}
