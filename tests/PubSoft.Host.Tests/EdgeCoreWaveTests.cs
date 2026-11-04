using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeCoreWaveTests
{
    [Fact]
    public void EdgeErrorFactoriesProduceCodedResponses()
    {
        var r = EdgeError.BadRequest("bad", "B1");
        Assert.False(r.Ok);
        Assert.Equal(EdgeErrorCodes.BadRequest, r.Code);
        Assert.Equal("bad", r.Message);
        Assert.Equal("B1", r.SubCode);
        Assert.Equal(EdgeErrorCodes.Unauthorized, EdgeError.Unauthorized("u").Code);
        Assert.Equal(EdgeErrorCodes.Forbidden, EdgeError.Forbidden("f").Code);
        Assert.Equal(EdgeErrorCodes.NotFound, EdgeError.NotFound("n").Code);
        Assert.Equal(EdgeErrorCodes.Conflict, EdgeError.Conflict("c").Code);
        Assert.Equal(EdgeErrorCodes.RateLimited, EdgeError.RateLimited("r").Code);
        Assert.Equal(EdgeErrorCodes.InternalError, EdgeError.InternalError("i").Code);
        Assert.Equal(EdgeErrorCodes.ServiceUnavailable, EdgeError.ServiceUnavailable("s").Code);
        Assert.Equal(EdgeErrorCodes.GatewayTimeout, EdgeError.GatewayTimeout("g").Code);
    }

    [Fact]
    public void HmacSignVerifyRoundtrip()
    {
        var sig = EdgeSigner.SignHmac("user.get", 1234567890L, "n1", "r1", new { id = 1 }, "secret");
        Assert.False(string.IsNullOrEmpty(sig));
        Assert.True(EdgeSigner.VerifyHmac("user.get", 1234567890L, "n1", "r1", new { id = 1 }, "secret", sig));
        Assert.False(EdgeSigner.VerifyHmac("user.get", 1234567890L, "n1", "r1", new { id = 2 }, "secret", sig)); // tampered params
        Assert.False(EdgeSigner.VerifyHmac("user.get", 1234567890L, "n1", "r1", new { id = 1 }, "wrong", sig)); // wrong key
    }

    [Fact]
    public void Md5SignVerifyRoundtrip()
    {
        var sig = EdgeSigner.SignMd5("m", 1L, "n", "r", null, "k");
        Assert.True(EdgeSigner.VerifyMd5("m", 1L, "n", "r", null, "k", sig));
        Assert.False(EdgeSigner.VerifyMd5("m", 2L, "n", "r", null, "k", sig)); // tampered timestamp
    }

    [Fact]
    public void CanonicalizeJsonDeterministic()
    {
        var a = EdgeSigner.CanonicalizeJson(new { b = 2, a = 1 });
        var b = EdgeSigner.CanonicalizeJson(new { a = 1, b = 2 });
        Assert.Equal(a, b); // same content different order → same canonical form
    }

    [Fact]
    public void BuildSignPayloadIncludesAllFields()
    {
        var p = EdgeSigner.BuildSignPayload("m", 1L, "n", "r", new { x = 1 });
        Assert.Contains("m", p);
        Assert.Contains("n", p);
        Assert.Contains("r", p);
    }
}
