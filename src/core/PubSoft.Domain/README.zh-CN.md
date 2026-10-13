# PubSoft.Domain

[English](README.md) | **简体中文**

`PubSoft.Domain` 提供框架无关的实体基类、scope 契约、仓储接口与数据范围元数据。不提供数据库映射、查询过滤器、当前用户实现或授权评估——EF 行为由 `PubSoft.EntityFrameworkCore` 可选提供。

## 实体与审计基类

`Entity<TKey>` 为应用控制主键的实体，`AggregateRoot<TKey>` 为聚合边界：

```csharp
using PubSoft.Domain;

public sealed class CatalogItem : FullAuditedAggregateRoot
{
    public string Name { get; set; } = string.Empty;
}
```

实体相等要求同运行时类型 + 非默认 ID——两个默认 ID 的瞬态实例不相等。基类不生成 ID、不阻止主键变更。

审计基类构造时初始化 `CreationTime` 为 UTC；`Audited*` 暴露创建者/修改者元数据；`FullAudited*` 增加软删元数据；聚合根变体实现 `IMultiTenant`（`FullAuditedEntity<TKey>` 无租户字段）。这些仅是数据字段与接口——本包不填充操作者 ID、不更新时间戳、不过滤行、不拦截删除。

## Scope 契约

`IMultiTenant`/`IMemberScoped`/`ISoftDelete` 描述实体字段；`ITenantContext`/`IMemberScopeContext` 为只读 scope 契约——本包不决定其填充方式。租户/成员的选择、可信来源与授权归宿主。

`DataScopeDimensionRegistry` 按序数不区分大小写存储维度名，`Dimensions` 返回只读快照，注册线程安全且拒绝空名；`DataScopeDimension` 将 allowed IDs 防御性复制为只读集合：

```csharp
var registry = new DataScopeDimensionRegistry();
registry.Add("store");

var scope = new DataScopeDimension(
    name: "store",
    isAll: false,
    allowedIds: [10, 11],
    currentId: 10);
```

维度快照与 `RoleDataScope.IsAll` 均为描述性数据——不授予访问、不解析 claims、不提供 admin bypass、不解析角色指派。

## 仓储契约

`IRepository<TEntity,TKey>`/`IReadOnlyRepository<TEntity,TKey>` 描述异步查询与持久化操作，本身不定义工作单元或事务策略。EF Core 适配器提供 scope 感知的仓储行为与查询过滤——数据库语义见其指南。

`RoleDataScope` 是租户感知的角色+维度+范围 ID 记录；schema 由 EF mapping 扩展 opt-in 提供（租户/角色索引 + 租户过滤）。它不是 RBAC 实现，不替代权限检查。

## Ambient Scope 上下文

`AmbientTenantContext`（`ITenantContext`）与 `AmbientMemberScopeContext`（`IMemberScopeContext`）基于 AsyncLocal，可注册单例——ambient 状态随 async 控制流隔离，实例生命周期不影响隔离性。`Change()` 只在当前控制流下游生效——callee 内的 `Change` 不会回流调用方，scope 注入必须在包裹 `await next()` 的层完成。`IsMemberScoped=true` 但无 `MemberId` 时 fail closed（查询匹配空集）。两者均不校验所设值的授权性——谁可改 scope 归宿主决策。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Domain`: `AggregateRoot<TKey>` `AmbientMemberScopeContext` `AmbientTenantContext` `AuditedAggregateRoot` `AuditedEntity<TKey>` `CreationAuditedAggregateRoot` `CreationAuditedEntity<TKey>` `DataScopeAccessEvaluator` `DataScopeDimension` `DataScopeDimensionRegistry` `Entity<TKey>` `FullAuditedAggregateRoot` `FullAuditedEntity<TKey>` `IDataScopeDimensionRegistry` `IMemberScopeContext` `IMemberScoped` `IMultiTenant` `IPubSoftSaveParticipant` `IReadOnlyRepository<TEntity, TKey>` `IRepository<TEntity, TKey>` `IRoleDataScopeProvider` `ISoftDelete` `ITenantContext` `PermissionGrantRecord` `PermissionModule` `PermissionRole` `RoleDataScope` `SubjectPermissionRole`
