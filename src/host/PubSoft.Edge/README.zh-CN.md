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

## OpenAPI（opt-in）

单端点 method 分发对 Swashbuckle 不可见，网关因此自产文档——由已注册的 `IEdgeHandler` 集合 + handler/params 的 XML 注释反射生成：

```csharp
builder.Services.AddEdgeGateway(o => o.EnablePseudoPaths = true);
builder.Services.AddEdgeOpenApi(builder.Configuration); // Edge:OpenApi
app.MapEdgeOpenApi("/rpc");
```

- `GET /openapi.json`——OpenAPI 3.0.3：真实 envelope 端点 + 每方法一条伪路径 + `x-edge-methods` 目录
- `GET /docs`——Swagger UI（CDN 资源，可用 `Edge:OpenApi:UiEnabled=false` 关闭）
- `EnablePseudoPaths`（默认关）——接受 `POST /rpc/{method}`，路径尾段覆盖 `envelope.method`；限流同样覆盖伪路径，文档只在开启时列出伪路径。`hmac`/`md5` 请求的签名串必须按**路径中的 method** 计算（网关在验签前覆盖 `envelope.method`）
- 文档暴露完整方法目录——**默认仅 Development 环境映射**（`DevelopmentOnly`）；非 dev 需显式置 false 并自行按网络/鉴权管控
- `/docs` 从 CDN 加载 Swagger UI（`SwaggerUiCssUrl`/`SwaggerUiBundleUrl`）——离线或高加固部署请自托管资源或 `UiEnabled=false`
- `subMessage` 携带原始上游/传输层细节（如 `HttpRequestException.Message`，可能包含内部主机名）——定位为集成方诊断字段，不是 C 端文案；响应直达终端用户时请在 handler/宿主层抑制

## 配置

- `Edge:Gateway:TimestampToleranceSeconds`——签名时间窗（默认 300 秒，0 关闭）
- `Edge:Gateway:EnablePseudoPaths`——接受 `POST /rpc/{method}` 伪路径（默认 false）
- `Edge:RateLimit:*`——按 clientId/方案/IP 的限流 + 失败限流
- `Edge:OpenApi:*`——文档/UI 路径、标题、clientId 示例
