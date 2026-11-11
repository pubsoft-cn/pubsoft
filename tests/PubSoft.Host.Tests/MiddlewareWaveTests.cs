using Microsoft.AspNetCore.Http;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Host.Tests;

/// <summary>Middleware invocation coverage via DefaultHttpContext.</summary>
public sealed class MiddlewareWaveTests
{
    private static readonly RequestDelegate Terminal = _ => Task.CompletedTask;

    [Fact]
    public async Task CorrelationIdSetsTraceIdentifier()
    {
        var mw = new CorrelationIdMiddleware(Terminal, new CorrelationIdOptions());
        var ctx = new DefaultHttpContext();
        ctx.TraceIdentifier = null!;
        await mw.InvokeAsync(ctx);
        Assert.False(string.IsNullOrEmpty(ctx.TraceIdentifier));
    }

    [Fact]
    public async Task CorrelationIdHonorsIncomingHeader()
    {
        var mw = new CorrelationIdMiddleware(Terminal, new CorrelationIdOptions());
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Correlation-ID"] = "ext-123";
        await mw.InvokeAsync(ctx);
        Assert.Equal("ext-123", CorrelationIdMiddleware.CurrentId(ctx));
    }

    [Fact]
    public async Task SecurityHeadersSetsDefaults()
    {
        var mw = new SecurityHeadersMiddleware(Terminal, new SecurityHeadersOptions());
        var ctx = new DefaultHttpContext();
        await mw.InvokeAsync(ctx);
        Assert.Equal("nosniff", ctx.Response.Headers.XContentTypeOptions.ToString());
    }
}
