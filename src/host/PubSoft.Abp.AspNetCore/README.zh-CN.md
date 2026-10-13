# PubSoft.Abp.AspNetCore

[English](README.md) | **简体中文**

可选 Volo.Abp 适配器：在 ABP 模块内注册 `PubSoft.AspNetCore` 的 scoped `IPrincipalSnapshotAccessor`。

## 注册

```csharp
[DependsOn(typeof(PubSoftAbpAspNetCoreModule))]
public sealed class HostModule : AbpModule { }
```

accessor 原样快照 `HttpContext.User`——认证标志、有序 claims 及原始 type/value/issuer；无请求时返回 `null`。不解释 claims、不解析 tenant/role、不配置授权。遗留的隐式 UoW、MVC convention 与静态 WebApplication 启动扩展刻意不注册。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Abp.AspNetCore`: `PubSoftAbpAspNetCoreModule` `PubSoftAbpAspNetCoreServiceCollectionExtensions`
