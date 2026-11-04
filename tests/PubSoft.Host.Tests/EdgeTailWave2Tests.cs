using Microsoft.Extensions.DependencyInjection;
using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeTailWave2Tests
{
    private sealed class MyHandler : IEdgeHandler
    {
        public string Method => "my.op";
        public EdgeAuthScheme[] Schemes => [EdgeAuthScheme.Hmac];
        public Task<EdgeRpcResponse> ExecuteAsync(EdgeRpcRequest r, EdgeContext c, CancellationToken ct)
            => Task.FromResult(EdgeRpcResponse.Success(null));
    }

    [Fact]
    public void AddEdgeHandlerWiresIntoRegistry()
    {
        var s = new ServiceCollection();
        s.AddEdgeHandler<MyHandler>().AddEdgeGateway();
        using var sp = s.BuildServiceProvider();
        var reg = sp.GetRequiredService<EdgeHandlerRegistry>();
        Assert.True(reg.TryGetHandler("my.op", out var h));
        Assert.Equal("my.op", h.Method);
        Assert.False(reg.TryGetHandler("no.such", out _));
    }

    [Fact]
    public void RegistryEmptyWithoutHandlers()
    {
        var s = new ServiceCollection();
        s.AddEdgeGateway();
        using var sp = s.BuildServiceProvider();
        Assert.False(sp.GetRequiredService<EdgeHandlerRegistry>().TryGetHandler("x", out _));
    }

    [Fact]
    public void EdgeOpenApiOptionsDefaults()
    {
        var o = new EdgeOpenApiOptions();
        Assert.Equal("/openapi.json", o.DocumentPath);
        Assert.Equal("/docs", o.UiPath);
        Assert.True(o.UiEnabled);
        Assert.Equal("Edge RPC", o.Title);
        Assert.NotNull(EdgeOpenApiOptions.SectionName);
    }

    [Fact]
    public void EdgeContextRecordEquality()
    {
        var a = new EdgeContext { ClientId = "c", Scheme = EdgeAuthScheme.Hmac };
        var b = new EdgeContext { ClientId = "c", Scheme = EdgeAuthScheme.Hmac };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a with { ClientId = "x" });
    }
}
