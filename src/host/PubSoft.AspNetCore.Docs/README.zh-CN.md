# PubSoft.AspNetCore.Docs

[English](README.md) | **简体中文**

面向 ASP.NET Core 服务的自托管 API 文档——从 `EndpointDataSource` + XML 注释生成
契约目录，可选语义 overlay 合并，附带嵌入式 docs 页与 TypeScript 客户端生成器
（`scripts/apigen.mjs`）。不依赖 Swashbuckle。

## 诚实边界

- 这是**内部交接文档**，不是 OpenAPI 实现——喂不了标准 OpenAPI 工具链
  （Swagger UI 导入、REST 插件、OpenAPI codegen）。替代 Swagger codegen 请用
  `scripts/apigen.mjs` 对 catalog 端点生成 TS client。
- catalog 暴露服务**全量路由表**——opt-in 且**默认仅 Development 环境映射**
  （`DevelopmentOnly`）；非 dev 需显式置 false 并配合 `RequireAuthorization`/网络门控。
- catalog 每进程构建一次并缓存；动态改路由后调 `Invalidate()` 重建。

## 注册

```csharp
builder.Services.AddPubSoftApiDocs(o =>
{
    o.Title = "My Service API";
    o.CatalogPath = "/docs/catalog";
    o.PagePath = "/docs";                 // null = 只要 JSON catalog
    o.OverlayPath = "configs/api-docs-overlay.json";
    o.RequireAuthorization = false;
    o.GroupBy = (method, path) => path.StartsWith("/api/report") ? "reports" : "misc";
    o.ExtraEndpoints.Add(new ApiDocExtraEndpoint { Method = "WS", Path = "/ws", ... });
});
app.MapPubSoftApiDocs();
```

## overlay 文件

```json
{
  "endpoints": {
    "GET /api/v2/report/overview": {
      "group": "reports", "note": "金额单位元", "auth": "open",
      "example": { "totalAmount": 52334754.25 }
    }
  },
  "extra": [
    { "method": "WS", "path": "/ws/v2", "group": "realtime", "summary": "订单推送" }
  ]
}
```

key 为 `"METHOD /path"`；`example` 同时驱动文档页渲染和 TS client 的
**示例驱动类型推断**。

## TS 客户端生成

```bash
node scripts/apigen.mjs http://localhost:5090/docs/catalog -o src/api/api-client.ts
```

产出 `api.getXxx()` / `api.postXxx()`；有 `example` 的端点推断出精确返回接口，
无示例的返回 `Promise<unknown>`。

## 安全说明

- 文档页对所有渲染内容（summary/note/example）做 HTML 实体转义——XML 注释和
  overlay 文本无法注入标记。
- MD/JSON 导出全部在**客户端**基于已加载 catalog 生成，不新增服务端暴露面；
  下载文件名做 `[\w.-]+` 过滤。
- `scripts/apigen.mjs` 把 catalog 当不可信数据处理：路由模板先转义 `` ` ``/`$`
  再还原参数插值；doc 注释转义 `*/`；example 只做**类型推断**，值不落进生成代码。
- overlay 路径来自宿主配置（非请求输入）；损坏的 overlay JSON 被捕获不阻断服务。
- `DevelopmentOnly`（默认 true）在非 Development 环境完全不映射；
  `RequireAuthorization` 为极少数非 dev 暴露场景追加鉴权门。catalog 为只读 GET，无 CSRF 面。
