---
purpose: package-level hosting guide for PubSoft.Edge.Client
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-14
---

# PubSoft.Edge.Client

Client SDK for Edge RPC — auto-signs requests for every credential scheme.

```csharp
// Hmac (B2B)
services.AddEdgeClient("https://edge.example.com", "my-client", "secret");

// Session (after login)
services.AddEdgeClientWithSession("https://edge.example.com", "my-client", sessionKey);

// Or directly:
var client = new EdgeClient(http, "my-client", "secret");
var dto = await client.InvokeAsync<MyDto>("demo.ping", new { itemId = 1 });
```

- `InvokeAsync<T>` / `InvokeVoidAsync` — throw `EdgeRpcException` on error envelopes
- `LastRequestId` — correlation id sent with the last call (echoed as `requestId`)
- `BuildRequest` / `GetSignPayload` — debugging helpers

## Trimmed/Native AOT consumption

The `object?` parameters / parameterless-`T` overloads serialize through runtime-type reflection. Trimmed or Native AOT callers use the explicit-metadata path: pre-serialize params with the caller's own source-generated context and pass response metadata — the envelope itself is serialized through the package-owned `EdgeEnvelopeJsonContext` without reflection:

```csharp
var parameters = JsonSerializer.SerializeToElement(dto, AppJsonContext.Default.MyParamsDto);
var result = await client.InvokeAsync("demo.ping", parameters, AppJsonContext.Default.MyResultDto);
await client.InvokeVoidAsync("demo.notify", parameters);
```

`JsonElement` params sign identically to their object equivalents: canonicalization operates on the element directly, so a pre-serialized payload cannot drift from its signature. `EdgeAuthScheme` member wire names are pinned (`hmac`/`session`/`none`/`md5`), independent of serializer naming-policy drift.

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**(options & records)**

`HelpLink`, `HResult`, `LastRequestId`, `Source`

<!-- pubsoft:api-surface:end -->
