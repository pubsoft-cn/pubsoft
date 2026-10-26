using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PubSoft.AspNetCore.Docs;

/// <summary>Registration and endpoint mapping for the PubSoft API docs catalog.</summary>
public static class ApiDocsExtensions
{
    /// <summary>Register the catalog service. Call <see cref="MapPubSoftApiDocs"/> to expose it.</summary>
    public static IServiceCollection AddPubSoftApiDocs(
        this IServiceCollection services, Action<ApiDocsOptions>? configure = null)
    {
        if (configure != null) services.Configure(configure);
        else services.Configure<ApiDocsOptions>(_ => { });
        services.AddSingleton<ApiDocsCatalogService>();
        return services;
    }

    /// <summary>
    /// Map the JSON catalog endpoint and (unless <see cref="ApiDocsOptions.PagePath"/> is null)
    /// the embedded docs page. Honours <see cref="ApiDocsOptions.RequireAuthorization"/>.
    /// Outside Development, nothing is mapped unless <see cref="ApiDocsOptions.DevelopmentOnly"/>
    /// is explicitly disabled.
    /// </summary>
    public static IEndpointRouteBuilder MapPubSoftApiDocs(this IEndpointRouteBuilder app)
    {
        var options = app.ServiceProvider.GetRequiredService<IOptions<ApiDocsOptions>>().Value;
        // fail fast at startup rather than per-request 500 when AddPubSoftApiDocs was skipped
        _ = app.ServiceProvider.GetRequiredService<ApiDocsCatalogService>();

        if (options.DevelopmentOnly
            && app.ServiceProvider.GetService<IHostEnvironment>()?.IsDevelopment() != true)
            return app;

        var catalog = app.MapGet(options.CatalogPath,
            (ApiDocsCatalogService svc) => svc.GetCatalog());

        if (options.PagePath is { } pagePath)
        {
            var title = System.Net.WebUtility.HtmlEncode(options.Title);
            var catalogPath = System.Net.WebUtility.HtmlEncode(options.CatalogPath);
            var page = app.MapGet(pagePath, () =>
            {
                var html = LoadPage().Replace("{{TITLE}}", title).Replace("{{CATALOG}}", catalogPath);
                return Results.Content(html, "text/html; charset=utf-8");
            });
            if (options.RequireAuthorization) { catalog.RequireAuthorization(); page.RequireAuthorization(); }
        }
        else if (options.RequireAuthorization)
        {
            catalog.RequireAuthorization();
        }
        return app;
    }

    private static string? _page;

    private static string LoadPage()
    {
        if (_page != null) return _page;
        var asm = typeof(ApiDocsExtensions).Assembly;
        using var s = asm.GetManifestResourceStream("PubSoft.AspNetCore.Docs.Assets.docs.html")
            ?? throw new InvalidOperationException("Embedded docs page missing");
        return _page = new StreamReader(s, Encoding.UTF8).ReadToEnd();
    }
}
