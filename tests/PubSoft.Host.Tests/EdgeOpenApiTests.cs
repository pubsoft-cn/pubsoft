using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeOpenApiTests
{
    private enum EchoKind { Alpha, Beta }

    private sealed class EchoParams
    {
        public string Text { get; set; } = "";
        public long? Count { get; set; }
        public EchoKind Kind { get; set; } = EchoKind.Alpha;
    }

    private sealed class EchoHandler : EdgeHandlerBase<EchoParams>
    {
        public override string Method => "demo.echo";
        protected override Task<EdgeRpcResponse> ExecuteAsync(EchoParams p, EdgeContext ctx, CancellationToken ct)
            => Task.FromResult(Success(new { text = p.Text }));
    }

    private sealed class EmptyParams { }

    private sealed class FailingHandler : EdgeHandlerBase<EmptyParams>
    {
        public static Func<Task<EdgeRpcResponse>>? Fault;
        public override string Method => "fail.upstream";
        public override EdgeAuthScheme[] Schemes => [EdgeAuthScheme.None];
        protected override Task<EdgeRpcResponse> ExecuteAsync(EmptyParams p, EdgeContext ctx, CancellationToken ct)
            => Fault is { } f ? f() : Task.FromResult(Success());
    }

    private sealed class AnonHandler : EdgeHandlerBase<EmptyParams>
    {
        public override string Method => "anon.ping";
        public override EdgeAuthScheme[] Schemes => [EdgeAuthScheme.None];
        protected override Task<EdgeRpcResponse> ExecuteAsync(EmptyParams p, EdgeContext ctx, CancellationToken ct)
            => Task.FromResult(Success(new { ok = true }));
    }

    private static IEdgeHandler[] Handlers => [new EchoHandler(), new AnonHandler()];

    private static async Task<IHost> StartHostAsync(
        bool pseudoPaths, int nonePermitLimit = 1000, bool mapOpenApi = true,
        string environment = "Development")
    {
        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment(environment);
                web.ConfigureServices(services =>
                {
                    if (pseudoPaths)
                        services.AddEdgeGateway(o => o.EnablePseudoPaths = true);
                    else
                        services.AddEdgeGateway();
                    services.AddEdgeInMemorySession();
                    services.AddEdgeRateLimiting(o => o.NonePermitLimit = nonePermitLimit);
                    services.AddEdgeHandler<EchoHandler>();
                    services.AddEdgeHandler<AnonHandler>();
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseEdgeRateLimiting("/rpc");
                    app.UseEdgeGateway("/rpc");
                    app.UseRouting();
                    if (mapOpenApi)
                        app.UseEndpoints(e => e.MapEdgeOpenApi("/rpc"));
                });
            })
            .StartAsync();
    }

    // ── document generation ──

    [Fact]
    public void Document_Should_List_Registered_Handlers_With_Typed_Params()
    {
        var doc = EdgeOpenApiDocument.Build(Handlers, "/rpc",
            new EdgeOpenApiOptions(), new EdgeGatewayOptions { EnablePseudoPaths = true });

        var methods = doc["x-edge-methods"]!.AsArray();
        var echo = methods.Select(m => (JsonObject)m!).Single(m => m!["method"]!.GetValue<string>() == "demo.echo");
        Assert.Equal("hmac", echo["schemes"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("session", echo["schemes"]!.AsArray()[1]!.GetValue<string>());

        var anon = methods.Select(m => (JsonObject)m!).Single(m => m!["method"]!.GetValue<string>() == "anon.ping");
        Assert.Equal("none", anon["schemes"]!.AsArray()[0]!.GetValue<string>());

        var echoParams = (JsonObject)doc["components"]!["schemas"]!["EchoParams"]!;
        var required = echoParams["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Contains("text", required);
        Assert.Contains("kind", required);
        Assert.DoesNotContain("count", required);

        var kind = (JsonObject)((JsonObject)echoParams["properties"]!)["kind"]!;
        Assert.Equal("string", kind["type"]!.GetValue<string>());
        Assert.Equal(new[] { "alpha", "beta" },
            kind["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void Document_Should_Emit_Pseudo_Paths_Only_When_Enabled()
    {
        var on = EdgeOpenApiDocument.Build(Handlers, "/rpc",
            new EdgeOpenApiOptions(), new EdgeGatewayOptions { EnablePseudoPaths = true });
        Assert.NotNull(on["paths"]!["/rpc/demo.echo"]);
        Assert.NotNull(on["paths"]!["/rpc/anon.ping"]);

        var off = EdgeOpenApiDocument.Build(Handlers, "/rpc",
            new EdgeOpenApiOptions(), new EdgeGatewayOptions());
        Assert.Null(off["paths"]!["/rpc/demo.echo"]);
        Assert.NotNull(off["paths"]!["/rpc"]);
    }

    [Fact]
    public void Document_Pseudo_Paths_Should_Carry_Per_Scheme_Ready_To_Send_Examples()
    {
        var doc = EdgeOpenApiDocument.Build(Handlers, "/rpc",
            new EdgeOpenApiOptions { ClientIdExample = "demo-app" },
            new EdgeGatewayOptions { EnablePseudoPaths = true });

        var media = (JsonObject)doc["paths"]!["/rpc/anon.ping"]!["post"]!["requestBody"]!["content"]!["application/json"]!;
        var props = (JsonObject)((JsonObject)media["schema"]!)["properties"]!;
        Assert.NotNull(props["clientId"]);
        Assert.NotNull(props["scheme"]);
        Assert.Null(props["method"]);

        var none = (JsonObject)((JsonObject)media["examples"]!)["none"]!["value"]!;
        Assert.Equal("demo-app", none["clientId"]!.GetValue<string>());
        Assert.Equal("none", none["scheme"]!.GetValue<string>());

        var echoExamples = (JsonObject)doc["paths"]!["/rpc/demo.echo"]!["post"]!["requestBody"]!["content"]!["application/json"]!["examples"]!;
        var session = (JsonObject)echoExamples["session"]!["value"]!;
        Assert.Equal("<sessionKey>", session["credential"]!.GetValue<string>());
        var hmacParams = (JsonObject)((JsonObject)echoExamples["hmac"]!["value"]!)["params"]!;
        Assert.Equal("", hmacParams["text"]!.GetValue<string>());
    }

    [Fact]
    public void Document_Envelope_Schema_Should_Match_Real_Contract()
    {
        var doc = EdgeOpenApiDocument.Build(Handlers, "/rpc", new EdgeOpenApiOptions(), new EdgeGatewayOptions());
        var envelope = (JsonObject)doc["components"]!["schemas"]!["EdgeRpcRequest"]!;

        var required = envelope["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "clientId", "method" }, required);

        var props = (JsonObject)envelope["properties"]!;
        Assert.Equal("int64", ((JsonObject)props["timestamp"]!)["format"]!.GetValue<string>());
        var schemeEnum = ((JsonObject)props["scheme"]!)["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "hmac", "session", "none", "md5" }, schemeEnum);
    }

    // ── endpoints ──

    [Fact]
    public async Task Document_And_Ui_Endpoints_Should_Serve()
    {
        using var host = await StartHostAsync(pseudoPaths: true);
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/openapi.json");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        Assert.NotNull(doc["paths"]!["/rpc/demo.echo"]);

        var ui = await client.GetAsync("/docs");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("SwaggerUIBundle", await ui.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Document_And_Ui_Endpoints_Should_Not_Map_Outside_Development()
    {
        using var host = await StartHostAsync(pseudoPaths: true, environment: "Production");
        var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/docs")).StatusCode);
    }

    [Fact]
    public async Task PseudoPath_Should_Dispatch_When_Enabled()
    {
        using var host = await StartHostAsync(pseudoPaths: true);
        var client = host.GetTestClient();

        var resp = await client.PostAsJsonAsync("/rpc/anon.ping", new
        {
            clientId = "test-client",
            scheme = "none",
            @params = new { },
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(EdgeJsonOptions.Default);
        Assert.True(body.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task PseudoPath_Should_Not_Match_When_Disabled()
    {
        using var host = await StartHostAsync(pseudoPaths: false, mapOpenApi: false);
        var client = host.GetTestClient();

        var resp = await client.PostAsJsonAsync("/rpc/anon.ping", new
        {
            clientId = "test-client",
            scheme = "none",
            @params = new { },
        });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PseudoPath_Should_Be_Rate_Limited()
    {
        using var host = await StartHostAsync(pseudoPaths: true, nonePermitLimit: 1);
        var client = host.GetTestClient();

        var envelope = new
        {
            clientId = "test-client",
            scheme = "none",
            @params = new { },
        };
        var first = await (await client.PostAsJsonAsync("/rpc/anon.ping", envelope))
            .Content.ReadFromJsonAsync<JsonElement>(EdgeJsonOptions.Default);
        Assert.True(first.GetProperty("ok").GetBoolean());

        var second = await (await client.PostAsJsonAsync("/rpc/anon.ping", envelope))
            .Content.ReadFromJsonAsync<JsonElement>(EdgeJsonOptions.Default);
        Assert.False(second.GetProperty("ok").GetBoolean());
        Assert.Equal("10429", second.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Handler_Should_Map_HttpRequestException_To_10503()
    {
        Assert.Equal("10503", await FaultCodeAsync(() => throw new HttpRequestException("downstream 502")));
    }

    [Fact]
    public async Task Handler_Should_Map_Timeout_To_10504()
    {
        Assert.Equal("10504", await FaultCodeAsync(() => throw new TaskCanceledException("timed out")));
    }

    [Fact]
    public async Task Error_Response_Should_Carry_SubCode_Only_When_Set()
    {
        var withSub = JsonSerializer.Serialize(
            EdgeRpcResponse.ErrorWithSub("10400", "bad", "identity.400"), EdgeJsonOptions.Default);
        Assert.Contains("\"subCode\":\"identity.400\"", withSub);

        var without = JsonSerializer.Serialize(
            EdgeRpcResponse.Error("10400", "bad"), EdgeJsonOptions.Default);
        Assert.DoesNotContain("subCode", without);
        Assert.DoesNotContain("subMessage", without);

        // EdgeException.SubCode/SubMessage flow through the base-class mapping.
        var handler = new FailingHandler();
        FailingHandler.Fault = () => throw new EdgeException("10400", "bad", "params.x", "field detail");
        try
        {
            var mapped = await handler.ExecuteAsync(
                new EdgeRpcRequest { Method = "fail.upstream" },
                new EdgeContext { ClientId = "c" },
                CancellationToken.None);
            Assert.Equal("params.x", mapped.SubCode);
            Assert.Equal("field detail", mapped.SubMessage);
        }
        finally
        {
            FailingHandler.Fault = null;
        }
    }

    private static async Task<string> FaultCodeAsync(Func<Task<EdgeRpcResponse>> fault)
    {
        var handler = new FailingHandler();
        FailingHandler.Fault = fault;
        try
        {
            var response = await handler.ExecuteAsync(
                new EdgeRpcRequest { Method = "fail.upstream" },
                new EdgeContext { ClientId = "test-client" },
                CancellationToken.None);
            return response.Code;
        }
        finally
        {
            FailingHandler.Fault = null;
        }
    }
}
