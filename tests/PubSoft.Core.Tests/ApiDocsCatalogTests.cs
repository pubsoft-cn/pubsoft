using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore.Docs;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ApiDocsCatalogTests
{
    [Fact]
    public async Task DevelopmentOnly_MapsNothingOutsideDevelopment()
    {
        await using var app = await BuildApp(_ => { });

        var paths = RoutePaths(app);
        Assert.DoesNotContain("/docs/catalog", paths);
        Assert.DoesNotContain("/docs", paths);
    }

    [Fact]
    public async Task DevelopmentOnlyDisabled_MapsCatalogAndPage()
    {
        await using var app = await BuildApp(o => o.DevelopmentOnly = false);

        var paths = RoutePaths(app);
        Assert.Contains("/docs/catalog", paths);
        Assert.Contains("/docs", paths);
    }

    [Fact]
    public async Task Catalog_ListsControllerRouteWithXmlDocs_AndSkipsOwnAndCatchAll()
    {
        await using var app = await BuildApp(o =>
        {
            o.DevelopmentOnly = false;
            o.ExtraEndpoints.Add(new ApiDocExtraEndpoint { Method = "WS", Path = "/ws/feed", Group = "realtime" });
        });

        var entries = CatalogEntries(app);
        var widget = Assert.Single(entries, e => e.GetProperty("path").GetString() == "/api/widgets/{id}");

        Assert.Equal("GET", widget.GetProperty("method").GetString());
        Assert.Equal("api/widgets", widget.GetProperty("group").GetString());
        Assert.Equal("open", widget.GetProperty("auth").GetString());
        Assert.Equal("Gets a widget.", widget.GetProperty("summary").GetString());
        Assert.Equal("id", widget.GetProperty("parameters")[0].GetProperty("name").GetString());
        Assert.Equal("WidgetDocs", widget.GetProperty("controller").GetString());

        // 目录自身端点与 catch-all 永远不入目录
        Assert.DoesNotContain(entries, e => e.GetProperty("path").GetString() is "/docs" or "/docs/catalog");
        Assert.DoesNotContain(entries, e => e.GetProperty("path").GetString()!.Contains('*'));

        // 手工补充的非 HTTP 条目入目录
        Assert.Contains(entries, e => e.GetProperty("path").GetString() == "/ws/feed"
            && e.GetProperty("group").GetString() == "realtime");
    }

    [Fact]
    public async Task Catalog_OverlayWinsOverDefaults()
    {
        var overlay = Path.Combine(Path.GetTempPath(), $"apidocs-overlay-{Guid.NewGuid():N}.json");
        File.WriteAllText(overlay, """
            { "endpoints": { "GET /api/widgets/{id}": { "group": "widgets", "note": "manual", "auth": "jwt" } } }
            """);
        try
        {
            await using var app = await BuildApp(o =>
            {
                o.DevelopmentOnly = false;
                o.OverlayPath = overlay;
            });

            var entries = CatalogEntries(app);
            var widget = Assert.Single(entries, e => e.GetProperty("path").GetString() == "/api/widgets/{id}");
            Assert.Equal("widgets", widget.GetProperty("group").GetString());
            Assert.Equal("jwt", widget.GetProperty("auth").GetString());
            Assert.Equal("manual", widget.GetProperty("note").GetString());
        }
        finally
        {
            File.Delete(overlay);
        }
    }

    // EndpointDataSource 只在 host 启动后才物化 app 级端点 —— TestServer 不占端口
    private static async Task<WebApplication> BuildApp(Action<ApiDocsOptions> configure)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(WidgetDocsController).Assembly);
        builder.Services.AddPubSoftApiDocs(configure);
        var app = builder.Build();
        app.MapControllers();
        app.MapFallback(() => "spa");
        app.MapPubSoftApiDocs();
        await app.StartAsync();
        return app;
    }

    private static List<string> RoutePaths(WebApplication app)
        => app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? "")
            .ToList();

    private static List<JsonElement> CatalogEntries(WebApplication app)
    {
        var catalog = app.Services.GetRequiredService<ApiDocsCatalogService>().GetCatalog();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(catalog));
        return doc.RootElement.GetProperty("groups")
            .EnumerateArray()
            .SelectMany(g => g.GetProperty("endpoints").EnumerateArray().Select(e => e.Clone()))
            .ToList();
    }
}

public sealed class WidgetDocsController : ControllerBase
{
    /// <summary>Gets a widget.</summary>
    /// <param name="id">Widget id.</param>
    [HttpGet("api/widgets/{id}")]
    public string GetWidget(long id) => "widget";
}
