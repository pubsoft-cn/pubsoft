using System.Text.Json;

namespace PubSoft.AspNetCore.Docs;

/// <summary>
/// Options for the PubSoft API docs catalog. The catalog exposes the service's full
/// route map — an internal/dev surface: mapped only in Development unless
/// <see cref="DevelopmentOnly"/> is disabled, and can be additionally gated with
/// <see cref="RequireAuthorization"/>.
/// </summary>
public sealed class ApiDocsOptions
{
    /// <summary>Path of the JSON catalog endpoint.</summary>
    public string CatalogPath { get; set; } = "/docs/catalog";

    /// <summary>
    /// Path of the rendered docs page (embedded asset). Null disables the page while
    /// keeping the JSON catalog.
    /// </summary>
    public string? PagePath { get; set; } = "/docs";

    /// <summary>Document title shown on the page and catalog.</summary>
    public string Title { get; set; } = "API Docs";

    /// <summary>
    /// Optional path (absolute, or relative to ContentRootPath) of a JSON overlay file
    /// adding semantic annotations per endpoint:
    /// <code>{ "endpoints": { "GET /x": { "group": "..", "note": "..", "auth": "jwt", "example": {...} } },
    ///          "extra": [ { "method": "WS", "path": "/ws", "summary": "..", "group": "..", "note": ".." } ] }</code>
    /// </summary>
    public string? OverlayPath { get; set; }

    /// <summary>
    /// Grouping rule for the page navigation. Receives (method, path); default groups
    /// by the first two path segments.
    /// </summary>
    public Func<string, string, string>? GroupBy { get; set; }

    /// <summary>
    /// Extra non-HTTP entries merged into the catalog (e.g. WebSocket protocols that
    /// endpoint enumeration cannot see).
    /// </summary>
    public List<ApiDocExtraEndpoint> ExtraEndpoints { get; } = new();

    /// <summary>
    /// When true, both catalog and page require authorization via the endpoint's
    /// auth metadata pipeline (e.g. an AuthorizeAttribute policy).
    /// </summary>
    public bool RequireAuthorization { get; set; }

    /// <summary>
    /// When true (default), <see cref="ApiDocsExtensions.MapPubSoftApiDocs"/> maps nothing
    /// outside the Development environment — the catalog exposes the full route map, so
    /// production exposure must be an explicit opt-in.
    /// </summary>
    public bool DevelopmentOnly { get; set; } = true;

    /// <summary>
    /// When true, endpoints are marked auth=required if their endpoint metadata
    /// contains authorization requirements; overlay "auth" always wins.
    /// </summary>
    public bool DetectAuthMetadata { get; set; } = true;

    /// <summary>
    /// When false (default), only controller actions are catalogued — catch-all
    /// fallbacks, health checks, and bare Map endpoints are treated as
    /// infrastructure noise. Set true for hosts built on minimal APIs.
    /// </summary>
    public bool IncludeNonActionEndpoints { get; set; }
}

/// <summary>Manual catalog entry for non-controller surfaces (WS protocols, raw maps).</summary>
public sealed class ApiDocExtraEndpoint
{
    public string Method { get; set; } = "WS";
    public string Path { get; set; } = "";
    public string? Summary { get; set; }
    public string? Group { get; set; }
    public string? Note { get; set; }
    public string Auth { get; set; } = "open";
    public JsonElement? Example { get; set; }
}
