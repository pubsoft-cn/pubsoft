using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeRateLimitTests
{
    private sealed class AnyHandler : EdgeHandlerBase<EdgeRateLimitTests.AnyParams>
    {
        public override string Method => "t.call";
        public override EdgeAuthScheme[] Schemes => [EdgeAuthScheme.None];
        protected override Task<EdgeRpcResponse> ExecuteAsync(AnyParams p, EdgeContext ctx, CancellationToken ct)
            => Task.FromResult(Success(new { ok = true }));
    }

    private sealed class FailHandler : EdgeHandlerBase<AnyParams>
    {
        public override string Method => "t.fail";
        public override EdgeAuthScheme[] Schemes => [EdgeAuthScheme.None];
        protected override Task<EdgeRpcResponse> ExecuteAsync(AnyParams p, EdgeContext ctx, CancellationToken ct)
            => Task.FromResult(EdgeError.InternalError("boom"));
    }

    private sealed class AnyParams { }

    private static async Task<IHost> StartHostAsync(EdgeRateLimitOptions options)
    {
        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddEdgeGateway();
                    services.AddEdgeRateLimiting(o =>
                    {
                        o.Enabled = options.Enabled;
                        o.NonePermitLimit = options.NonePermitLimit;
                        o.FailureRateLimitEnabled = options.FailureRateLimitEnabled;
                        o.NoneFailurePermitLimit = options.NoneFailurePermitLimit;
                        o.WindowSeconds = options.WindowSeconds;
                        o.FailureWindowSeconds = options.FailureWindowSeconds;
                    });
                    services.AddEdgeHandler<AnyHandler>();
                    services.AddEdgeHandler<FailHandler>();
                });
                web.Configure(app =>
                {
                    app.UseEdgeRateLimiting("/rpc");
                    app.UseEdgeGateway("/rpc");
                });
            })
            .StartAsync();
    }

    private static object Req(string method) => new
    {
        clientId = "c1",
        method,
        scheme = "none",
        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        @params = new { },
    };

    private static async Task<JsonElement> PostAsync(HttpClient client, string method)
        => await (await client.PostAsJsonAsync("/rpc", Req(method), EdgeJsonOptions.Default))
            .Content.ReadFromJsonAsync<JsonElement>(EdgeJsonOptions.Default);

    [Fact]
    public async Task Total_Limit_Should_Return_10429_When_Exceeded()
    {
        using var host = await StartHostAsync(new EdgeRateLimitOptions { NonePermitLimit = 2 });
        var client = host.GetTestClient();

        Assert.True((await PostAsync(client, "t.call")).GetProperty("ok").GetBoolean());
        Assert.True((await PostAsync(client, "t.call")).GetProperty("ok").GetBoolean());
        var third = await PostAsync(client, "t.call");
        Assert.Equal("10429", third.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Failure_Limit_Should_Count_Only_Failed_Calls()
    {
        using var host = await StartHostAsync(new EdgeRateLimitOptions
        {
            NonePermitLimit = 100,
            NoneFailurePermitLimit = 2,
        });
        var client = host.GetTestClient();

        // 2 failures → 3rd should be failure-rate-limited
        await PostAsync(client, "t.fail");
        await PostAsync(client, "t.fail");
        var third = await PostAsync(client, "t.call");
        Assert.Equal("10429", third.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Successful_Calls_Should_Not_Count_As_Failures()
    {
        using var host = await StartHostAsync(new EdgeRateLimitOptions
        {
            NonePermitLimit = 100,
            NoneFailurePermitLimit = 1,
        });
        var client = host.GetTestClient();

        Assert.True((await PostAsync(client, "t.call")).GetProperty("ok").GetBoolean());
        Assert.True((await PostAsync(client, "t.call")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Disabled_Limit_Should_Pass_Through()
    {
        using var host = await StartHostAsync(new EdgeRateLimitOptions { Enabled = false, NonePermitLimit = 1 });
        var client = host.GetTestClient();
        Assert.True((await PostAsync(client, "t.call")).GetProperty("ok").GetBoolean());
        Assert.True((await PostAsync(client, "t.call")).GetProperty("ok").GetBoolean());
        Assert.True((await PostAsync(client, "t.call")).GetProperty("ok").GetBoolean());
    }
}
