using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AspNetCoreWave2Tests
{
    [Fact]
    public async Task CorrelationIdGeneratesAndEchoes()
    {
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx);
        Assert.False(string.IsNullOrEmpty(ctx.TraceIdentifier));
        Assert.NotNull(CorrelationIdMiddleware.CurrentId(ctx));
    }

    [Fact]
    public async Task CorrelationIdReusesInboundHeader()
    {
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Request-Id"] = "inbound-1";
        ctx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx);
        Assert.Equal("inbound-1", CorrelationIdMiddleware.CurrentId(ctx));
    }

    [Fact]
    public async Task CorrelationIdDisabledSkips()
    {
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask,
            new CorrelationIdOptions { Enabled = false });
        var ctx = new DefaultHttpContext();
        await middleware.InvokeAsync(ctx);
        Assert.False(ctx.Response.Headers.ContainsKey("X-Correlation-Id"));
    }
}
