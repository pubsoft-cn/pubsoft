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
