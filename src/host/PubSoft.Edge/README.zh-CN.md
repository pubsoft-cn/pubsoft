# PubSoft.Edge

服务端 Edge RPC 网关——单 POST 入口按 `method` 分发到 handler，支持可插拔凭证方案、会话管理与限流。

作为小程序、H5、三方应用与设备的统一客户端入口。

## 接入

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

## Handler

```csharp
public class PingHandler : EdgeHandlerBase<PingParams>
{
    public override string Method => "demo.ping";

    protected override Task<EdgeRpcResponse> ExecuteAsync(PingParams p, EdgeContext ctx, CancellationToken ct)
        => Task.FromResult(Success(new { ok = true }));
}
```

- `IEdgeHandler.Schemes` 声明接受的方案（默认 Hmac + Session）；`[EdgeScheme(...)]` 覆盖
- `EdgeContext` 暴露 `ClientId`、`RequestId`、`Session`（+ `UserId`/`MemberId`/`Roles`/`Token`）
- 业务错误抛 `EdgeException(code, message)`

## 凭证方案

| 方案 | 凭证 | 用途 |
|---|---|---|
| `hmac` | HMAC-SHA256 签名 | 三方应用、设备（默认） |
| `session` | sessionKey | 小程序/H5 登录后 |
| `md5` | MD5 签名 | 无法用 HMAC 的遗留客户端 |
| `none` | — | 登录/公开端点 |

## 会话

`EdgeSessionManager` 签发 `{clientId}:{appId}:{guid}`；`IEdgeSessionStore` 可插拔（默认内存——多实例请接分布式存储）。

## 配置

- `Edge:Gateway:TimestampToleranceSeconds`——签名时间窗（默认 300 秒，0 关闭）
- `Edge:RateLimit:*`——按 clientId/方案/IP 的限流 + 失败限流
