# PubSoft.AspNetCore.Docs

**English** | [简体中文](README.zh-CN.md)

Self-hosted API documentation for ASP.NET Core services — a Swashbuckle-free
catalog generated from `EndpointDataSource` + XML doc comments, merged with an
optional semantic overlay JSON, plus an embedded docs page and a companion
TypeScript client generator (`scripts/apigen.mjs`).

## Honest boundaries

- This is an **internal handoff doc**, not an OpenAPI implementation. It cannot feed
  standard OpenAPI tooling (Swagger UI import, REST-client plugins, OpenAPI codegen).
  For a cleaner alternative to Swagger codegen, use `scripts/apigen.mjs` against the
  catalog endpoint.
- The catalog exposes the service's **entire route map**. It is opt-in and
  **Development-only by default** (`DevelopmentOnly`); for non-dev exposure set it
  false and combine with `RequireAuthorization = true` or a network gate.
- The catalog is built once per process and cached; call
  `ApiDocsCatalogService.Invalidate()` after dynamic route changes.

## Security notes

- The docs page escapes **all** rendered endpoint content (`summary`/`note`/`example`)
  through HTML-entity encoding — XML doc comments and overlay text cannot inject markup.
- MD/JSON export is generated **client-side** from the already-fetched catalog — no
  additional server surface. Download filenames are sanitized to `[\w.-]+`.
- `scripts/apigen.mjs` treats the catalog as untrusted data: path parameters are
  re-interpolated only after escaping `` ` ``/`$` in route patterns, doc comments are
  `*/`-escaped, and example payloads drive **type inference only** — their values are
  never emitted as code.
- The overlay path comes from host configuration (not request input); corrupt overlay
  JSON is caught and never blocks serving.
- `DevelopmentOnly` (default true) skips mapping entirely outside Development;
  `RequireAuthorization` adds an auth gate for the rare non-dev exposure. The catalog
  is otherwise a read-only GET with no CSRF surface.

## Registration

```csharp
builder.Services.AddPubSoftApiDocs(o =>
{
    o.Title = "My Service API";
    o.CatalogPath = "/docs/catalog";
    o.PagePath = "/docs";                 // null = JSON catalog only
    o.OverlayPath = "configs/api-docs-overlay.json";
    o.RequireAuthorization = false;       // true → RequireAuthorization() on both endpoints
    o.GroupBy = (method, path) => path.StartsWith("/api/report") ? "reports" : "misc";
    o.ExtraEndpoints.Add(new ApiDocExtraEndpoint
    {
        Method = "WS", Path = "/ws", Group = "realtime",
        Summary = "live order push",
        Note = "connect ?clientId=…; broadcasts {type:\"order\",data:{…}}"
    });
});
app.MapPubSoftApiDocs();
```

## Overlay file

```json
{
  "endpoints": {
    "GET /api/v2/report/overview": {
      "group": "reports",
      "note": "amounts are in yuan; subject: global|merchant:{id}|outlet:{id}",
      "auth": "open",
      "example": { "totalAmount": 52334754.25, "orderCount": 3863533 }
    }
  },
  "extra": [
    { "method": "WS", "path": "/ws/v2", "group": "realtime",
      "summary": "order push", "note": "send {\"type\":\"heartbeat\"} → ack" }
  ]
}
```

Keys are `"METHOD /path"`. `example` payloads drive both the rendered docs page and
**example-driven type inference** in the TS client generator.

## TypeScript client generation

```bash
node scripts/apigen.mjs http://localhost:5090/docs/catalog -o src/api/api-client.ts
```

Emits `api.getXxx(...)`, `api.postXxx(...)`, typed via `example` inference; query
params come from controller signatures. Endpoints without an example return
`Promise<unknown>`.

## Public API surface

- `PubSoft.AspNetCore.Docs`: `ApiDocExtraEndpoint` `ApiDocsCatalogService`
  `ApiDocsExtensions` `ApiDocsOptions`
