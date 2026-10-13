# PubSoft.Authorization

[English](README.md) | **简体中文**

`PubSoft.Authorization` 提供框架无关的权限元数据、查找与基于宿主授权数据的评估。不认证调用方、不存储授权、不解析用户角色。`PubSoft.AspNetCore` 提供 opt-in Minimal API 端点过滤器——非全局中间件，MVC controller 由独立的 MVC filter 覆盖。

## 定义与注册权限

实现 `IPermissionDefinitionProvider` 并在应用组装期传给 `PermissionDefinitionRegistry`：

```csharp
using PubSoft.Authorization;

public sealed class UserPermissions : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> GetDefinitions() =>
    [
        new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query | PermissionFlags.Create,
            DisplayName = "Users",
            ApiPaths = ["GET /api/users", "POST /api/users"],
            FrontendRoutes = ["/users", "/users/:userId"]
        }
    ];
}

var catalog = new PermissionDefinitionRegistry([new UserPermissions()]);
var matches = catalog.GetByApiPath("GET", "/api/users");
```

注册表校验模块键、非零已知 flag 位、extension 元数据、重复 module/flag 键、路径语法与重复路径，然后快照定义与路由集合。API 模式用 `METHOD /path`；前端与 API 模板接受整段 `:name` 或 `{name}`。查找按段数精确匹配，忽略 HTTP 方法大小写与尾斜杠，字面段按序数比较；query string 与 fragment 不参与查找。

`PermissionModuleRegistry` 聚合模块 provider 并解析显示名/描述。`PermissionModuleAttribute`、`RequirePermissionAttribute`、`RequireCustomPermissionAttribute`、`RequireExtPermissionAttribute`、`PermissionExemptAttribute` 等元数据标注是框架无关的注解——本包不扫描程序集也不执行。

## Opt-In Minimal API 强制

注册可信 subject accessor 与宿主提供的 grant/context 服务，然后对 route handler 或 route group 应用 `AddPubSoftPermissionAuthorization()`。只有带 `RequirePermissionAttribute`/`RequireCustomPermissionAttribute`/`RequireExtPermissionAttribute` 元数据的端点才被评估：

```csharp
app.MapGet("/api/users", ListUsers)
    .WithMetadata(new RequirePermissionAttribute("users", PermissionFlags.Query))
    .AddPubSoftPermissionAuthorization();
```

无可信 issuer 解析出的 subject 返回 401；解析租户与 `ITenantContext.TenantId` 不完全匹配返回 403；任一声明要求未获授权返回 403；通过则继续 handler。`PermissionExemptAttribute` 与 `IAllowAnonymous` 优先于同端点要求元数据（Minimal endpoint filter 与 MVC filter 共用同一判定）——豁免端点即使同时带要求标注也不做权限检查。无任何要求元数据的端点不受影响。认证中间件、租户选择/授权、权限定义与 grant provider 注册归宿主。

## 显式授权评估

实现 `IPermissionGrantProvider` 返回该主体在精确租户 scope 下的有效授权。角色成员关系、授权聚合与存储由该 provider 承担：

```csharp
var evaluator = new PermissionAuthorizationService(catalog, grantProvider);
bool allowed = await evaluator.IsGrantedAsync(
    subjectId: 42,
    tenantId: 7,
    moduleKey: "users",
    requiredFlags: PermissionFlags.Query,
    cancellationToken);
```

`PermissionGrant` 快照一个模块的标准 flag、extension 位掩码与 custom 权限名。`IsGrantedAsync`/`IsCustomPermissionGrantedAsync`/`IsExtensionPermissionGrantedAsync` 都要求已声明的模块/权限与精确匹配的授权。授权只在精确 module + 精确可空 tenant scope 下合并；host 域授权不流入租户 scope，租户授权也不流入 host 域。标准要求须满足全部请求位；custom 名按序数比较；extension 要求每一位都须已声明且已授予。无效 subject/tenant ID 与未知或未声明权限返回 `false`；provider 异常传播，绝不视为允许。

**无隐式管理员 bypass**——provider 须按宿主显式的角色/subject 指派规则返回有效授权；调用方传入的 subject 与 tenant scope 须由可信认证/租户边界确立。

## 推导要求

`PermissionRequirementDeriver` 可把 HTTP 方法与 POST action 名词映射为 `PermissionFlags`，并可由显式键、最长路由前缀映射或唯一 controller 名后缀解析模块。这些是有启发式输入的 catalog 便利功能——应用需要稳定语义时显式钉住模块键与要求，不要把名称推导当作安全边界。

授权持久化/替换事务、角色成员关系、匿名端点行为与策略优先级归宿主决策。evaluator 无 admin bypass，不判定租户切换授权。Minimal API filter 只执行显式权限元数据，不从路由或 controller 推导要求。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Authorization`: `AutoPermissionOptions` `IPermissionDefinitionProvider` `IPermissionGrantProvider` `IPermissionModuleProvider` `PermissionAuthorizationService` `PermissionDefinition` `PermissionDefinitionRegistry` `PermissionExemptAttribute` `PermissionFlags` `PermissionGrant` `PermissionModuleAttribute` `PermissionModuleRegistry` `PermissionRequirementDeriver` `RequireCustomPermissionAttribute` `RequireExtPermissionAttribute` `RequirePermissionAttribute`
