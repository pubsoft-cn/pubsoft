using System.Text.Json;
using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeSignerTests
{
    private const string Secret = "test-secret-key-32chars-minimum";

    private static EdgeRpcRequest SignedRequest(EdgeAuthScheme scheme, string credential = "")
        => new()
        {
            ClientId = "demo-app",
            Method = "demo.ping",
            Scheme = scheme,
            Credential = credential,
            Timestamp = 1758700000000,
            Nonce = "n1",
            RequestId = "req-1",
            Params = JsonSerializer.SerializeToElement(new { itemId = 123 }, EdgeJsonOptions.Default),
        };

    [Fact]
    public void Hmac_Sign_And_Verify_Should_Match()
    {
        var sign = EdgeSigner.SignHmac("demo.ping", 1758700000000, "n1", "req-1", new { itemId = 123 }, Secret);
        Assert.True(EdgeSigner.VerifyHmac("demo.ping", 1758700000000, "n1", "req-1", new { itemId = 123 }, Secret, sign));
    }

    [Fact]
    public void Hmac_RequestId_Is_Signed()
    {
        var sign = EdgeSigner.SignHmac("m", 1, "n", "req-A", new { x = 1 }, Secret);
        Assert.False(EdgeSigner.VerifyHmac("m", 1, "n", "req-B", new { x = 1 }, Secret, sign));
        Assert.True(EdgeSigner.VerifyHmac("m", 1, "n", "req-A", new { x = 1 }, Secret, sign));
    }

    [Fact]
    public void Hmac_Verify_With_Wrong_Signature_Should_Fail()
    {
        Assert.False(EdgeSigner.VerifyHmac("m", 1, "n", null, new { x = 1 }, Secret, "AAAA"));
    }

    [Fact]
    public void Hmac_Verify_With_Empty_Signature_Should_Fail()
    {
        Assert.False(EdgeSigner.VerifyHmac("m", 1, "n", null, new { x = 1 }, Secret, ""));
        Assert.False(EdgeSigner.VerifyHmac("m", 1, "n", null, new { x = 1 }, Secret, null!));
    }

    [Fact]
    public void Hmac_Sign_Should_Be_Independent_Of_Property_Declaration_Order()
    {
        var a = EdgeSigner.SignHmac("m", 1, "n", null, new { b = 2, a = 1 }, Secret);
        var b = EdgeSigner.SignHmac("m", 1, "n", null, new { a = 1, b = 2 }, Secret);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Hmac_Sign_Should_Sort_Keys_In_Ordinal_Order()
    {
        // Property names are camelCased during canonicalization, then sorted ordinally.
        var payload = EdgeSigner.BuildSignPayload("m", 1, "n", null, new { zebra = 1, alpha = 2, Beta = 3 });
        Assert.Contains("\"beta\":3", payload);
        Assert.True(payload.IndexOf("\"alpha\"", StringComparison.Ordinal) < payload.IndexOf("\"beta\"", StringComparison.Ordinal));
        Assert.True(payload.IndexOf("\"beta\"", StringComparison.Ordinal) < payload.IndexOf("\"zebra\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Hmac_Sign_Should_Handle_Chinese_And_Unicode()
    {
        var sign = EdgeSigner.SignHmac("m", 1, "n", null, new { name = "中文", emoji = "🚗" }, Secret);
        Assert.True(EdgeSigner.VerifyHmac("m", 1, "n", null, new { name = "中文", emoji = "🚗" }, Secret, sign));
    }

    [Fact]
    public void Hmac_Sign_Should_Handle_Nested_Objects_And_Arrays()
    {
        var sign = EdgeSigner.SignHmac("m", 1, "n", null,
            new { user = new { name = "a", tags = new[] { "x", "y" } }, items = new object[] { new { b = 2, a = 1 } } }, Secret);
        Assert.True(EdgeSigner.VerifyHmac("m", 1, "n", null,
            new { user = new { name = "a", tags = new[] { "x", "y" } }, items = new object[] { new { b = 2, a = 1 } } }, Secret, sign));
    }

    [Fact]
    public void Hmac_Sign_Should_Exclude_Null_But_Keep_Zero_False_Empty()
    {
        var withNull = EdgeSigner.SignHmac("m", 1, "n", null, new { a = 1, b = (string?)null, c = 0, d = false, e = "" }, Secret);
        var without = EdgeSigner.SignHmac("m", 1, "n", null, new { a = 1, c = 0, d = false, e = "" }, Secret);
        Assert.Equal(withNull, without);
    }

    [Fact]
    public void Md5_Sign_And_Verify_Should_Match()
    {
        var sign = EdgeSigner.SignMd5("demo.ping", 1, "n", "r", new { itemId = 1 }, Secret);
        Assert.Matches("^[0-9a-f]{32}$", sign);
        Assert.True(EdgeSigner.VerifyMd5("demo.ping", 1, "n", "r", new { itemId = 1 }, Secret, sign));
        Assert.False(EdgeSigner.VerifyMd5("demo.ping", 1, "n", "r", new { itemId = 2 }, Secret, sign));
    }

    [Fact]
    public void Md5_And_Hmac_Are_Not_Cross_Compatible()
    {
        var md5 = EdgeSigner.SignMd5("m", 1, "n", null, new { x = 1 }, Secret);
        var hmac = EdgeSigner.SignHmac("m", 1, "n", null, new { x = 1 }, Secret);
        Assert.False(EdgeSigner.VerifyHmac("m", 1, "n", null, new { x = 1 }, Secret, md5));
        Assert.False(EdgeSigner.VerifyMd5("m", 1, "n", null, new { x = 1 }, Secret, hmac));
    }

    [Fact]
    public void Verify_Hmac_Request_Should_Succeed()
    {
        var req = SignedRequest(EdgeAuthScheme.Hmac);
        req.Credential = EdgeSigner.SignHmac(req.Method, req.Timestamp, req.Nonce, req.RequestId, req.Params, Secret);
        var result = EdgeSigner.Verify(req, Secret);
        Assert.True(result.Success);
    }

    [Fact]
    public void Verify_Md5_Request_Should_Succeed()
    {
        var req = SignedRequest(EdgeAuthScheme.Md5);
        req.Credential = EdgeSigner.SignMd5(req.Method, req.Timestamp, req.Nonce, req.RequestId, req.Params, Secret);
        var result = EdgeSigner.Verify(req, Secret);
        Assert.True(result.Success);
    }

    [Fact]
    public void Verify_Without_Nonce_Should_Fail()
    {
        var req = SignedRequest(EdgeAuthScheme.Hmac);
        req.Nonce = "";
        req.Credential = EdgeSigner.SignHmac(req.Method, req.Timestamp, req.Nonce, req.RequestId, req.Params, Secret);
        var result = EdgeSigner.Verify(req, Secret);
        Assert.False(result.Success);
        Assert.Contains("nonce", result.ErrorMessage);
    }

    [Fact]
    public void Verify_Session_Scheme_Should_Defer_To_Session_Manager()
    {
        var result = EdgeSigner.Verify(SignedRequest(EdgeAuthScheme.Session, "sess-key"), Secret);
        Assert.False(result.Success);
        Assert.Contains("Session", result.ErrorMessage);
    }

    [Fact]
    public void Verify_None_Scheme_Should_Pass()
    {
        Assert.True(EdgeSigner.Verify(SignedRequest(EdgeAuthScheme.None), Secret).Success);
    }

    [Fact]
    public void Canonicalize_Should_Produce_Stable_Output()
    {
        var a = EdgeSigner.CanonicalizeJson(new { b = new { d = 1, c = 2 }, a = new[] { 3, 2, 1 } });
        var b = EdgeSigner.CanonicalizeJson(new { a = new[] { 3, 2, 1 }, b = new { c = 2, d = 1 } });
        Assert.Equal(a, b);
        Assert.Equal("{\"a\":[3,2,1],\"b\":{\"c\":2,\"d\":1}}", a);
    }
}
