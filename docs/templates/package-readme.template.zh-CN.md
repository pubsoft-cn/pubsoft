---
purpose: <一句话——这个包是干什么的>
audience: <集成方开发 | 运维 | 评审>
owner: <人或团队>
last-reviewed: <YYYY-MM-DD>
---

# PubSoft.<PackageName>

一句话定位——这个包提供什么、保障什么边界。

> 双语强制：本文件（zh-CN）+ `README.md`（en，节序一致）。
> 新增包须在根 `README.md` 总览表挂号。
> front-matter 为必备元数据——`purpose/audience/owner/last-reviewed` 喂季度文档复审。

## 范围与保障（Scope and Guarantees）

强制什么、委托给宿主什么——信任边界必须点名。
写明 fail-closed 默认值与显式 opt-in 开关（如 `AllowInsecure*`）。

## 接入（Setup）

```csharp
// 最小注册示例——services.AddPubSoft<Feature>(...)
```

## 选项（Options）

| 选项 | 默认 | 含义 |
|---|---|---|
| `Xxx` | `...` | ... |

## 宿主残余责任（Residual Host Responsibilities）

本包刻意不做的事——端点鉴权、密钥保管、出方向策略、留存等。
绝不得暗示包未提供的覆盖。

## 公共 API 面

由 `PublicAPI.Unshipped.txt` 生成——`dotnet format analyzers` 同步。
