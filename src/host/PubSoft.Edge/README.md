---
purpose: package-level hosting guide for PubSoft.Edge
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-14
---

# PubSoft.Edge

Server-side Edge RPC gateway — a single POST endpoint dispatching `method` to handlers with pluggable credential schemes, session management, and rate limiting.

Serves as the uniform client entry point for mini-programs, H5 pages, third-party apps, and devices.

## Setup

```csharp
builder.Services.AddEdgeGateway();
builder.Services.AddEdgeInMemorySession(TimeSpan.FromHours(2));
builder.Services.AddSingleton<IEdgeSignKeyProvider, MySignKeyProvider>();
builder.Services.AddEdgeRateLimiting(builder.Configuration);
builder.Services.AddEdgeHandler<PingHandler>();

var app = builder.Build();
app.UseEdgeRateLimiting("/rpc");
app.UseEdgeGateway("/rpc");
```

## Handlers

```csharp
public class PingHandler : EdgeHandlerBase<PingParams>
{
    public override string Method => "demo.ping";

    protected override Task<EdgeRpcResponse> ExecuteAsync(PingParams p, EdgeContext ctx, CancellationToken ct)
        => Task.FromResult(Success(new { ok = true }));
}
```

- `IEdgeHandler.Schemes` declares accepted schemes (default Hmac + Session); `[EdgeScheme(...)]` overrides
- `EdgeContext` exposes `ClientId`, `RequestId`, `Session` (+ `UserId`/`MemberId`/`Roles`/`Token`)
- Throw `EdgeException(code, message)` for business errors

## Credential schemes

| Scheme | Credential | Use |
|---|---|---|
| `hmac` | HMAC-SHA256 signature | Third-party apps, devices (default) |
| `session` | sessionKey | Mini-program/H5 after login |
| `md5` | MD5 signature | Legacy clients unable to use HMAC |
| `none` | — | Login/public endpoints |

## Sessions

`EdgeSessionManager` issues `{clientId}:{appId}:{guid}` keys; `IEdgeSessionStore` is the pluggable store (in-memory default — use a distributed store for multi-instance).

## Remote sign keys

`AddRemoteEdgeSignKeys` registers a singleton `RemoteEdgeSignKeyProvider` implementing both `IEdgeSignKeyProvider` and `IEdgeSchemePolicy` — for hosts that hold edge sign keys in a remote credential registry instead of static configuration:

```csharp
builder.Services.AddRemoteEdgeSignKeys(o =>
{
    o.FetchKeys = ct => registry.GetMyEdgeCredentialsAsync(ct); // host-injected; return null on outage
    o.LocalKeys = builder.Configuration.GetSection("Edge:SignKeys")
        .Get<Dictionary<string, string>>();                      // optional fallback
});
```

- `FetchKeys` (required) — pulls this service's `EdgeSignKeyEntry` bundle (`ClientId`/`SecretKey`/optional `Scheme` pin). The registry call typically authenticates the caller itself (self-scoped); supply that credential inside the delegate — PubSoft does not bind to a registry SDK.
- Refresh: once at startup, then every `RefreshInterval` (default 5 min). A failed or null fetch keeps the last good pool. Registry entries win over `LocalKeys` on conflict.
- Until the first successful fetch, a key miss triggers a synchronous pull (bounded by `RefreshOnMissTimeout`, default 10 s; disable via `RefreshOnMiss=false`) — prevents cold-start rejections.
- `Scheme` pins map to `IEdgeSchemePolicy` (`hmac`/`md5`/`session`/`none`; unknown values leave the scheme unconstrained). Unknown clientIds return null and the gateway fails closed.
- Note: entries hold plaintext symmetric keys — this is key *escrow* for third-party signing protocols, not service-to-service authentication. Key rotation propagates within one `RefreshInterval`.

## OpenAPI (opt-in)

A single method-dispatch endpoint is invisible to Swashbuckle, so the gateway can emit its own document — generated from the registered `IEdgeHandler` set plus XML doc comments on handlers/params:

```csharp
builder.Services.AddEdgeGateway(o => o.EnablePseudoPaths = true);
builder.Services.AddEdgeOpenApi(builder.Configuration); // Edge:OpenApi
app.MapEdgeOpenApi("/rpc");
```

- `GET /openapi.json` — OpenAPI 3.0.3: real envelope endpoint + one pseudo-path per method + `x-edge-methods` catalog
- `GET /docs` — Swagger UI (CDN assets, disable via `Edge:OpenApi:UiEnabled=false`)
- `EnablePseudoPaths` (default off) — accepts `POST /rpc/{method}` where the path tail overrides `envelope.method`; rate limiting covers pseudo-paths, and the document only lists them when enabled. For `hmac`/`md5` requests the signature must be computed over the *path* method (the gateway overrides `envelope.method` before verifying).
- The document exposes the full method catalog — mapped **only in Development** by default (`DevelopmentOnly`); set it false for non-dev exposure and gate by network/auth yourself
- `/docs` loads Swagger UI from a CDN (`SwaggerUiCssUrl`/`SwaggerUiBundleUrl`) — self-host the assets or set `UiEnabled=false` for air-gapped or hardened deployments
- `subMessage` carries raw upstream/transport detail (e.g. `HttpRequestException.Message`, which may include internal hostnames) — treat it as integrator-facing diagnostics, not C-end copy; suppress it at the handler/host layer if the response reaches end users

## Configuration

- `Edge:Gateway:TimestampToleranceSeconds` — signature timestamp window (default 300, 0 disables)
- `Edge:Gateway:EnablePseudoPaths` — accept `POST /rpc/{method}` pseudo-paths (default false)
- `Edge:RateLimit:*` — per-clientId/scheme/IP limits + failure limiting
- `Edge:OpenApi:*` — document/UI paths, title, clientId example

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**(options & records)**

`ClientId`, `ClientIdExample`, `ClientPermitLimit`, `Description`, `DevelopmentOnly`, `DocumentPath`, `Enabled`, `EnablePseudoPaths`, `ExpiresAt`, `FailurePermitLimit`, `FailureRateLimitEnabled`, `FailureWindowSeconds`, `FetchKeys`, `HelpLink`, `HmacPermitLimit`, `HResult`, `HttpContext`, `IsVerified`, `LocalKeys`, `Md5PermitLimit`, `MemberId`, `NoneFailurePermitLimit`, `NonePermitLimit`, `OpenId`, `QueueLimit`, `RefreshInterval`, `RefreshOnMiss`, `RefreshOnMissTimeout`, `RequestId`, `Roles`, `Scheme`, `SecretKey`, `Session`, `SessionKey`, `SessionPermitLimit`, `Source`, `SwaggerUiBundleUrl`, `SwaggerUiCssUrl`, `TenantId`, `TimestampToleranceSeconds`, `Title`, `Token`, `UiEnabled`, `UiPath`, `UserId`, `Version`, `WindowSeconds`

<!-- pubsoft:api-surface:end -->
