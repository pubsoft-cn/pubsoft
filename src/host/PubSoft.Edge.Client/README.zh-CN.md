# PubSoft.Edge.Client

Edge RPC 客户端 SDK——自动完成各凭证方案的签名。

```csharp
// Hmac（B2B）
services.AddEdgeClient("https://edge.example.com", "my-client", "secret");

// Session（登录后）
services.AddEdgeClientWithSession("https://edge.example.com", "my-client", sessionKey);

// 或直接构造：
var client = new EdgeClient(http, "my-client", "secret");
var dto = await client.InvokeAsync<MyDto>("demo.ping", new { itemId = 1 });
```

- `InvokeAsync<T>` / `InvokeVoidAsync`——错误信封抛 `EdgeRpcException`
- `LastRequestId`——上一次调用的关联 id（回显为 `requestId`）
- `BuildRequest` / `GetSignPayload`——调试辅助
