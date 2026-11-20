using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
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

    [Fact]
    public async Task CorrelationIdKeepsInboundSpanAsParent()
    {
        const string traceId = "fbe13a8493c655cdaa63f19080c322ed";
        const string callerSpan = "00f067aa0ba902b7";
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["traceparent"] = $"00-{traceId}-{callerSpan}-01";
        ctx.Response.Body = new MemoryStream();
        // AsyncLocal set inside the middleware flows down to _next, not back to the
        // caller - read it where a real consumer (the propagation handler) would.
        (string TraceId, string SpanId, string? ParentSpanId)? ambient = null;
        var pipeline = new CorrelationIdMiddleware(_ =>
        {
            ambient = CorrelationAmbient.Current;
            return Task.CompletedTask;
        });

        await pipeline.InvokeAsync(ctx);

        Assert.NotNull(ambient);
        Assert.Equal(traceId, ambient.Value.TraceId);
        Assert.Equal(callerSpan, ambient.Value.ParentSpanId);
        Assert.False(string.IsNullOrEmpty(ambient.Value.SpanId));
    }

    [Fact]
    public async Task PropagationPrefersActivityCurrentOverAmbient()
    {
        CorrelationAmbient.Current = ("deadbeefdeadbeefdeadbeefdeadbeef", "aaaabbbbccccdddd", null);
        using var activity = new Activity("request")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://downstream/");

        await InvokePropagation(request);

        var traceparent = Assert.Single(request.Headers.GetValues("traceparent"));
        Assert.Equal(
            $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}",
            traceparent);
        Assert.DoesNotContain("deadbeef", traceparent);
    }

    [Fact]
    public async Task PropagationFallsBackToAmbientWithoutW3cActivity()
    {
        CorrelationAmbient.Current = ("abcd1234abcd1234abcd1234abcd1234", "1122334455667788", "99aabbccddee0011");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://downstream/");

        await InvokePropagation(request);

        Assert.Equal(
            "00-abcd1234abcd1234abcd1234abcd1234-1122334455667788-01",
            Assert.Single(request.Headers.GetValues("traceparent")));
        Assert.Equal(
            "abcd1234abcd1234abcd1234abcd1234",
            Assert.Single(request.Headers.GetValues("X-Correlation-Id")));
    }

    private static async Task InvokePropagation(HttpRequestMessage request)
    {
        var handler = new CorrelationPropagationHandler { InnerHandler = new EchoHandler() };
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(request, CancellationToken.None);
    }

    private sealed class EchoHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
