using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeTailWaveTests
{
    private sealed class MyHandler : EdgeHandlerBase<object>
    {
        public override string Method => "my.op";
        protected override Task<EdgeRpcResponse> ExecuteAsync(object p, EdgeContext ctx, CancellationToken ct)
            => Task.FromResult(EdgeRpcResponse.Success(new { ok = true }));
    }

    [Fact]
    public void EdgeContextDerivesFromSession()
    {
        var session = new EdgeSession { UserId = "u", MemberId = "m", OpenId = "o", TenantId = "t", Roles = ["r1"], Token = "tk" };
        var ctx = new EdgeContext { Scheme = EdgeAuthScheme.Session, ClientId = "c", RequestId = "r", IsVerified = true, Session = session };
        Assert.Equal("u", ctx.UserId);
        Assert.Equal("m", ctx.MemberId);
        Assert.Equal("o", ctx.OpenId);
        Assert.Equal("t", ctx.TenantId);
        Assert.Equal(["r1"], ctx.Roles);
        Assert.Equal("tk", ctx.Token);
        Assert.True(ctx.IsVerified);
    }

    [Fact]
    public void EdgeContextNoSessionDefaults()
    {
        var ctx = new EdgeContext();
        Assert.Null(ctx.UserId);
        Assert.Empty(ctx.Roles);
        Assert.Equal(EdgeAuthScheme.None, ctx.Scheme);
        Assert.False(ctx.IsVerified);
    }

    [Fact]
    public void EdgeHandlerBaseDefaultSchemes()
    {
        var h = new MyHandler();
        Assert.Equal("my.op", h.Method);
        Assert.Equal([EdgeAuthScheme.Hmac, EdgeAuthScheme.Session], h.Schemes);
    }

    [Fact]
    public void EdgeExceptionCarriesCodeAndSub()
    {
        var e = new EdgeException("10400", "bad", "B1", "detail");
        Assert.Equal("10400", e.Code);
        Assert.Equal("B1", e.SubCode);
        Assert.Equal("detail", e.SubMessage);
    }
}
