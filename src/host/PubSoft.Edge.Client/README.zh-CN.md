---
purpose: PubSoft.Edge.Client 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-14
---

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

## Trimmed/Native AOT 消费方式

`object?` 参数与无 metadata 的 `T` 重载走运行时类型反射序列化。trimmed/Native AOT 调用方应使用显式 metadata 路径：params 用调用方自己的源生成 context 预序列化，response 传 `JsonTypeInfo<T>`——信封本身由包内 `EdgeEnvelopeJsonContext` 无反射序列化：

```csharp
var parameters = JsonSerializer.SerializeToElement(dto, AppJsonContext.Default.MyParamsDto);
var result = await client.InvokeAsync("demo.ping", parameters, AppJsonContext.Default.MyResultDto);
await client.InvokeVoidAsync("demo.notify", parameters);
```

`JsonElement` params 与等价 object 参数签名完全一致：canonicalization 直接作用于 element，预序列化 payload 不可能与签名漂移。`EdgeAuthScheme` 成员 wire name 已钉死（`hmac`/`session`/`none`/`md5`），不受 serializer naming-policy 漂移影响。
