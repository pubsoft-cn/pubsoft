using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore.Docs;

var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddControllers();
builder.Services.AddPubSoftApiDocs(options =>
{
    // smoke runs under the default Production environment — exercise the explicit opt-in
    options.DevelopmentOnly = false;
});
await using var app = builder.Build();
app.MapControllers();
app.MapPubSoftApiDocs();
// app-level endpoints materialize into EndpointDataSource only once the host starts
await app.StartAsync();

var endpoints = app.Services.GetRequiredService<EndpointDataSource>()
    .Endpoints.OfType<RouteEndpoint>().ToList();
if (endpoints.All(e => e.RoutePattern.RawText != "/docs/catalog") ||
    endpoints.All(e => e.RoutePattern.RawText != "/docs"))
    throw new InvalidOperationException("Docs endpoints were not mapped.");

var catalog = app.Services.GetRequiredService<ApiDocsCatalogService>().GetCatalog();
var json = JsonSerializer.Serialize(catalog);
using var doc = JsonDocument.Parse(json);
var listed = doc.RootElement.GetProperty("groups")
    .EnumerateArray()
    .SelectMany(g => g.GetProperty("endpoints").EnumerateArray())
    .Select(e => e.GetProperty("path").GetString())
    .ToList();
if (!listed.Contains("/api/widgets/{id}"))
    throw new InvalidOperationException("Controller route missing from docs catalog.");
if (listed.Any(p => p is "/docs" or "/docs/catalog"))
    throw new InvalidOperationException("Docs catalog lists its own endpoints.");

Console.WriteLine("Isolated PubSoft.AspNetCore.Docs consumer smoke check passed.");

public sealed class SmokeWidgetController : ControllerBase
{
    /// <summary>Gets a widget.</summary>
    [HttpGet("api/widgets/{id}")]
    public string GetWidget(long id) => "widget";
}
