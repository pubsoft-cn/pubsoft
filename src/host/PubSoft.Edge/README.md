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

## Configuration

- `Edge:Gateway:TimestampToleranceSeconds` — signature timestamp window (default 300, 0 disables)
- `Edge:RateLimit:*` — per-clientId/scheme/IP limits + failure limiting
