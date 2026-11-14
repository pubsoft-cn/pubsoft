using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;


/// <summary>
/// DI and endpoint extensions for the Edge OpenAPI surface.
/// </summary>
public static class EdgeOpenApiExtensions
{
    /// <summary>Configures the OpenAPI document surface.</summary>
    public static IServiceCollection AddEdgeOpenApi(this IServiceCollection services, Action<EdgeOpenApiOptions> configure)
    {
        services.Configure(configure);
        return services;
    }

    /// <summary>Binds OpenAPI options from configuration (Edge:OpenApi).</summary>
    public static IServiceCollection AddEdgeOpenApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EdgeOpenApiOptions>(configuration.GetSection(EdgeOpenApiOptions.SectionName));
        return services;
    }

    /// <summary>
    /// Maps the OpenAPI document (<see cref="EdgeOpenApiOptions.DocumentPath"/>) and, when
    /// <see cref="EdgeOpenApiOptions.UiEnabled"/>, the Swagger UI page (<see cref="EdgeOpenApiOptions.UiPath"/>).
    /// The document reflects the handlers registered in DI at first request.
    /// Outside Development, nothing is mapped unless <see cref="EdgeOpenApiOptions.DevelopmentOnly"/>
    /// is explicitly disabled.
    /// <code>
    /// app.MapEdgeOpenApi("/rpc");
    /// </code>
    /// </summary>
    public static IEndpointRouteBuilder MapEdgeOpenApi(this IEndpointRouteBuilder endpoints, string gatewayPath = "/rpc")
    {
        var opts = endpoints.ServiceProvider.GetService<IOptions<EdgeOpenApiOptions>>()?.Value
            ?? new EdgeOpenApiOptions();
        var gwOpts = endpoints.ServiceProvider.GetService<IOptions<EdgeGatewayOptions>>()?.Value
            ?? new EdgeGatewayOptions();
        var path = gatewayPath.TrimEnd('/');

        if (opts.DevelopmentOnly
            && endpoints.ServiceProvider.GetService<IHostEnvironment>()?.IsDevelopment() != true)
            return endpoints;

        var document = new Lazy<string>(() => EdgeOpenApiDocument
            .Build(endpoints.ServiceProvider.GetServices<IEdgeHandler>(), path, opts, gwOpts)
            .ToJsonString());

        endpoints.MapGet(opts.DocumentPath, () => Results.Content(document.Value, "application/json"));

        if (opts.UiEnabled)
        {
            var html = SwaggerUiHtml(opts);
            endpoints.MapGet(opts.UiPath, () => Results.Content(html, "text/html"));
        }

        return endpoints;
    }

    private static string SwaggerUiHtml(EdgeOpenApiOptions opts)
    {
        var title = System.Net.WebUtility.HtmlEncode(opts.Title);
        var css = System.Net.WebUtility.HtmlEncode(opts.SwaggerUiCssUrl);
        var js = System.Net.WebUtility.HtmlEncode(opts.SwaggerUiBundleUrl);
        var docUrl = JsonSerializer.Serialize(opts.DocumentPath);
        return $$"""
            <!DOCTYPE html><html><head><meta charset="utf-8"><title>{{title}}</title>
            <link rel="stylesheet" href="{{css}}"></head>
            <body><div id="ui"></div>
            <script src="{{js}}"></script>
            <script>SwaggerUIBundle({url:{{docUrl}},dom_id:"#ui",tryItOutEnabled:true,displayRequestDuration:true});</script>
            </body></html>
            """;
    }
}
