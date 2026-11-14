using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeGatewayTests
{
    private const string Secret = "test-secret-32chars-minimum-000";

    private sealed class TestKeyProvider : IEdgeSignKeyProvider
    {
        public string? GetSecretKey(string clientId) => clientId == "test-client" ? Secret : null;
        public bool ShowSignDebug => true;
    }

    private sealed class EchoParams { public string? Text { get; set; } }

    private sealed class EchoHandler : EdgeHandlerBase<EchoParams>
    {
        public override string Method => "echo.test";
        public override EdgeAuthScheme[] Schemes => [EdgeAuthScheme.Hmac, EdgeAuthScheme.Session, EdgeAuthScheme.Md5];
        protected override Task<EdgeRpcResponse> ExecuteAsync(EchoParams p, EdgeContext ctx, CancellationToken ct)
            => Task.FromResult(Success(new { text = p.Text, clientId = ctx.ClientId, userId = ctx.UserId }));
    }

    private sealed class LoginParams { }
    private sealed class LoginHandler : EdgeHandlerBase<LoginParams>
    {
        public override string Method => "auth.login";
        public override EdgeAuthScheme[] Schemes => [EdgeAuthScheme.None];
        protected override Task<EdgeRpcResponse> ExecuteAsync(LoginParams p, EdgeContext ctx, CancellationToken ct)
            => Task.FromResult(Success(new { ok = true }));
    }

    private sealed class SchemeLockedHandler : EdgeHandlerBase<EchoParams>
    {
        public override string Method => "locked.method";
        public override EdgeAuthScheme[] Schemes => [EdgeAuthScheme.Session];
        protected override Task<EdgeRpcResponse> ExecuteAsync(EchoParams p, EdgeContext ctx, CancellationToken ct)
            => Task.FromResult(Success(new { ok = true }));
    }

    private static async Task<IHost> StartHostAsync(Action<IServiceCollection>? configure = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddEdgeGateway();
                    services.AddEdgeInMemorySession();
                    services.AddSingleton<IEdgeSignKeyProvider, TestKeyProvider>();
                    services.AddEdgeHandler<EchoHandler>();
                    services.AddEdgeHandler<LoginHandler>();
                    services.AddEdgeHandler<SchemeLockedHandler>();
                    configure?.Invoke(services);
                });
                web.Configure(app => app.UseEdgeGateway("/rpc"));
            })
            .StartAsync();
        return host;
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/rpc", body, EdgeJsonOptions.Default);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(EdgeJsonOptions.Default);
    }

    private static object SignedRequest(EdgeAuthScheme scheme, string method, object? parameters = null,
        long? timestamp = null, string? requestId = "r1", string? credential = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = "n1";
        var cred = credential ?? (scheme == EdgeAuthScheme.Md5
            ? EdgeSigner.SignMd5(method, ts, nonce, requestId, parameters, Secret)
            : EdgeSigner.SignHmac(method, ts, nonce, requestId, parameters, Secret));
        return new
        {
            clientId = "test-client",
            method,
            scheme = scheme.ToString().ToLowerInvariant(),
            credential = cred,
            timestamp = ts,
            nonce,
            requestId,
            @params = parameters ?? new { },
        };
    }

    [Fact]
    public async Task Gateway_Hmac_Should_Dispatch_To_Handler()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, SignedRequest(EdgeAuthScheme.Hmac, "echo.test", new { text = "hi" }));
        Assert.True(resp.GetProperty("ok").GetBoolean());
        Assert.Equal("hi", resp.GetProperty("data").GetProperty("text").GetString());
        Assert.Equal("test-client", resp.GetProperty("data").GetProperty("clientId").GetString());
        Assert.Equal("r1", resp.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task Gateway_Hmac_With_Invalid_Signature_Should_Return_10401()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, SignedRequest(EdgeAuthScheme.Hmac, "echo.test", new { text = "hi" }, credential: "bad"));
        Assert.False(resp.GetProperty("ok").GetBoolean());
        Assert.Equal("10401", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Gateway_Md5_Should_Dispatch_To_Handler()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, SignedRequest(EdgeAuthScheme.Md5, "echo.test", new { text = "dev" }));
        Assert.True(resp.GetProperty("ok").GetBoolean());
        Assert.Equal("dev", resp.GetProperty("data").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Gateway_Md5_With_Invalid_Signature_Should_Return_10401()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, SignedRequest(EdgeAuthScheme.Md5, "echo.test", new { text = "x" }, credential: "deadbeef"));
        Assert.Equal("10401", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Gateway_Stale_Timestamp_Should_Return_10400()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var stale = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var resp = await PostAsync(client, SignedRequest(EdgeAuthScheme.Hmac, "echo.test", new { }, timestamp: stale));
        Assert.Equal("10400", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Gateway_Session_Should_Dispatch_With_Session_Context()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();

        var sessionManager = host.Services.GetRequiredService<EdgeSessionManager>();
        var key = await sessionManager.CreateAsync("test-client", "app", "jwt", userId: "u-9");

        var resp = await PostAsync(client, new
        {
            clientId = "test-client",
            method = "echo.test",
            scheme = "session",
            credential = key,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            @params = new { text = "hi" },
        });
        Assert.True(resp.GetProperty("ok").GetBoolean());
        Assert.Equal("u-9", resp.GetProperty("data").GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Gateway_Session_With_Invalid_Key_Should_Return_10401()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, new
        {
            clientId = "test-client",
            method = "echo.test",
            scheme = "session",
            credential = "bogus",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            @params = new { },
        });
        Assert.Equal("10401", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Gateway_None_Should_Dispatch_To_Login_Handler()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, new
        {
            clientId = "test-client",
            method = "auth.login",
            scheme = "none",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            @params = new { },
        });
        Assert.True(resp.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Gateway_Unknown_Method_Should_Return_10404()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, SignedRequest(EdgeAuthScheme.Hmac, "nope.missing"));
        Assert.Equal("10404", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Gateway_Scheme_Rejected_By_Handler_Should_Return_10401()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, SignedRequest(EdgeAuthScheme.Hmac, "locked.method"));
        Assert.Equal("10401", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Gateway_None_Scheme_On_Session_Only_Method_Should_Return_10401()
    {
        // Fail-closed: an anonymous scheme must not bypass a handler's declared schemes.
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, new
        {
            clientId = "test-client",
            method = "locked.method",
            scheme = "none",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            @params = new { },
        });
        Assert.Equal("10401", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Gateway_Missing_ClientId_Should_Return_10400()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        var resp = await PostAsync(client, new { method = "echo.test", scheme = "none", @params = new { } });
        Assert.Equal("10400", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Gateway_NonRpcPath_Should_Pass_Through()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddEdgeGateway();
                    services.AddSingleton<IEdgeSignKeyProvider, TestKeyProvider>();
                });
                web.Configure(app =>
                {
                    app.UseEdgeGateway("/rpc");
                    app.Run(ctx => ctx.Response.WriteAsync("fallthrough"));
                });
            })
            .StartAsync();

        var resp = await host.GetTestClient().GetStringAsync("/health");
        Assert.Equal("fallthrough", resp);
    }
}
