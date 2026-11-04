using System.Net;
using System.Text.Json;
using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeClientTests
{
    private sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return handler(request);
        }
    }

    private static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public void BuildRequestHmacSignsCredentialAndNonce()
    {
        var client = new EdgeClient(new HttpClient(), "cid", "secret", EdgeAuthScheme.Hmac);
        var req = client.BuildRequest("user.get", new { id = 1 });
        Assert.Equal("cid", req.ClientId);
        Assert.Equal("user.get", req.Method);
        Assert.Equal(EdgeAuthScheme.Hmac, req.Scheme);
        Assert.False(string.IsNullOrEmpty(req.Credential));
        Assert.False(string.IsNullOrEmpty(req.Nonce));
        Assert.False(string.IsNullOrEmpty(req.RequestId));
        Assert.Equal(1, req.Params.GetProperty("id").GetInt32());
    }

    [Fact]
    public void BuildRequestSessionUsesSessionKeyWithoutNonce()
    {
        var client = new EdgeClient(new HttpClient(), "cid", "session-key-1");
        var req = client.BuildRequest("user.get", null);
        Assert.Equal(EdgeAuthScheme.Session, req.Scheme);
        Assert.Equal("session-key-1", req.Credential);
    }

    [Fact]
    public void BuildRequestNoneHasNoCredential()
    {
        var client = new EdgeClient(new HttpClient(), "cid", EdgeAuthScheme.None);
        var req = client.BuildRequest("ping", null);
        Assert.Equal(EdgeAuthScheme.None, req.Scheme);
        Assert.Equal(string.Empty, req.Credential);
    }

    [Theory]
    [InlineData(EdgeAuthScheme.Hmac, true)]
    [InlineData(EdgeAuthScheme.Md5, true)]
    [InlineData(EdgeAuthScheme.Session, false)]
    [InlineData(EdgeAuthScheme.None, false)]
    public void GetSignPayloadOnlyForSignedSchemes(EdgeAuthScheme scheme, bool expectPayload)
    {
        var client = scheme == EdgeAuthScheme.Session
            ? new EdgeClient(new HttpClient(), "cid", "sk")
            : new EdgeClient(new HttpClient(), "cid", "sk", scheme);
        Assert.Equal(expectPayload, client.GetSignPayload("m", null) is not null);
    }

    [Fact]
    public async Task InvokeAsyncDeserializesData()
    {
        var stub = new StubHttp(_ => JsonResponse(new
        {
            ok = true,
            code = "0",
            message = "",
            data = new { name = "n1" },
            requestId = "r1",
        }));
        var client = new EdgeClient(new HttpClient(stub) { BaseAddress = new Uri("https://edge.test") }, "cid", "secret", EdgeAuthScheme.Hmac);
        var result = await client.InvokeAsync<Dictionary<string, string>>("user.get", new { id = 1 });
        Assert.Equal("n1", result!["name"]);
        Assert.NotNull(client.LastRequestId);
        Assert.Contains("user.get", stub.LastBody);
    }

    [Fact]
    public async Task InvokeAsyncRpcErrorThrowsEdgeRpcException()
    {
        var stub = new StubHttp(_ => JsonResponse(new { ok = false, code = "E403", message = "denied" }));
        var client = new EdgeClient(new HttpClient(stub) { BaseAddress = new Uri("https://edge.test") }, "cid", "secret");
        var ex = await Assert.ThrowsAsync<EdgeRpcException>(() => client.InvokeAsync<string>("m", null));
        Assert.Equal("E403", ex.Code);
        Assert.Contains("denied", ex.Message);
    }

    [Fact]
    public async Task InvokeAsyncHttpErrorThrowsWithStatusCode()
    {
        var stub = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new EdgeClient(new HttpClient(stub) { BaseAddress = new Uri("https://edge.test") }, "cid", "secret");
        var ex = await Assert.ThrowsAsync<EdgeRpcException>(() => client.InvokeAsync<string>("m", null));
        Assert.Equal("500", ex.Code);
    }

    [Fact]
    public async Task InvokeVoidSendsRequestWithoutReturn()
    {
        var stub = new StubHttp(_ => JsonResponse(new { ok = true, code = "0", message = "" }));
        var client = new EdgeClient(new HttpClient(stub) { BaseAddress = new Uri("https://edge.test") }, "cid", "secret");
        await client.InvokeVoidAsync("ping", new { t = 1 });
        Assert.Contains("ping", stub.LastBody);
        Assert.NotNull(client.LastRequestId);
    }
}
