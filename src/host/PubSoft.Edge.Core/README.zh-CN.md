---
purpose: PubSoft.Edge.Core 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-14
---

# PubSoft.Edge.Core

Edge RPC 基础件：请求/响应信封、凭证方案、规范化签名与共享错误码。

## 请求信封

```json
{
  "clientId": "demo-app",
  "method": "demo.ping",
  "scheme": "hmac",
  "credential": "BASE64-SIGNATURE",
  "timestamp": 1758700000000,
  "nonce": "a1b2c3",
  "requestId": "req-7f3a9e",
  "params": { "itemId": 123 },
  "version": "1.0"
}
```

| 字段 | 含义 |
|---|---|
| `clientId` | 已注册调用方标识 |
| `method` | RPC 方法（`domain.action`） |
| `scheme` | `hmac` / `session` / `none` / `md5` |
| `credential` | 签名值或 sessionKey，按 scheme 解释 |
| `timestamp` | Unix 毫秒（签名方案超出容差窗口即拒绝） |
| `nonce` | 一次性随机串（签名方案必填） |
| `requestId` | 可选客户端关联 id——入签名、响应回显、注入追踪 |
| `params` | 方法参数 |
| `version` | 可选契约版本 |

## 签名

规范化签名串（换行分隔）：`method`、`timestamp`、`nonce`、`requestId`、`canonicalJson(params)`。

- `hmac`：`Base64(HMAC-SHA256(payload, secretKey))`
- `md5`：`HexLower(MD5(payload + secretKey))`——面向无法用 HMAC 的客户端的遗留方案

规范化 JSON：递归字典序排序、紧凑输出、剔除 `null`/`undefined`（0/false/"" 仍参与签名）。

`EdgeSigner` 提供 `SignHmac`/`VerifyHmac`/`SignMd5`/`VerifyMd5`/`BuildSignPayload`/`CanonicalizeJson` 及统一 `Verify(request, secretKey)` 分发入口。
