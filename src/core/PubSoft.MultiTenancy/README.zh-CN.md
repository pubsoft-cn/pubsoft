# PubSoft.MultiTenancy

[English](README.md) | **简体中文**

`PubSoft.MultiTenancy` 提供框架无关的租户模型与管理 DTO/服务契约。依赖 `PubSoft.Domain`；不提供 HTTP 租户解析器、当前租户访问器、管理员授权或应用服务实现。

## 租户模型

用应用分配的 ID 与稳定 code 创建租户：

```csharp
using PubSoft.MultiTenancy;

var tenant = new Tenant(
    id: 42,
    code: "store-a",
    name: "Store A",
    displayName: "Store Alpha");
```

`Code`/`Name` 必填、自动 trim，分别限 64/100 字符。`Code` 无更新方法——改租户 code 需要宿主级迁移策略。`DisplayName` trim 限 100 字符，缺省/空白时用 `Name`。`Description` 可选，trim 限 200 字符。`CreationTime` 初始化为 UTC。`Update` 改 name/displayName/description，`SetEnabled` 改启用标志。

可选 `PubSoft.EntityFrameworkCore` 租户映射独立于本包——应用租户 schema（含 code 唯一约束）；provider 与迁移仍由宿主选择配置。

## 管理契约

`TenantDto`/`CreateTenantInput`/`UpdateTenantInput`/`ITenantAppService` 只定义数据形状与异步操作。本包不实现持久化、租户列表可见性、删除行为或授权——谁可创建/更新/列出/删除租户及租户生命周期规则归宿主强制。

本模型不是可信租户选择机制。请求头、路由值或用户提交的租户 ID 都不构成切换 scope 的授权——解析当前租户与授权租户切换属于显式配置的宿主身份/授权边界。

## Ambient Scope 上下文

`PubSoft.Domain` 提供 AsyncLocal 驱动的 `AmbientTenantContext`（`ITenantContext`）与 `AmbientMemberScopeContext`（`IMemberScopeContext`）。注册为单例——ambient 状态随 async 控制流隔离：

```csharp
services.AddSingleton<ITenantContext, AmbientTenantContext>();
services.AddSingleton<IMemberScopeContext, AmbientMemberScopeContext>();
```

请求/job 适配器从可信来源调一次 `Change(tenantId)`；返回的 scope 在 dispose 时还原前值。为端点设置租户 scope 必须包裹端点调用（`using (ctx.Change(id)) await next()`）——callee 内设置的 ambient 值不会回流到调用方 frame。`IsMemberScoped=true` 但无 `MemberId` 时 fail closed（查询匹配空集）。两者不校验所设值的授权性——谁可改 scope 归宿主授权决策。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.MultiTenancy`: `CreateTenantInput` `ITenantAppService` `Tenant` `TenantDto` `UpdateTenantInput`
