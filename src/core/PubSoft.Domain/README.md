# PubSoft.Domain

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Domain` contains framework-neutral entity bases, scope contracts, repository interfaces, and data-scope metadata. It does not provide database mappings, query filters, a current-user implementation, or authorization evaluation. EF behavior is opt-in through `PubSoft.EntityFrameworkCore`.

## Entities and Audit Bases

Derive `Entity<TKey>` for an entity with an application-controlled key, or `AggregateRoot<TKey>` for an aggregate boundary:

```csharp
using PubSoft.Domain;

public sealed class CatalogItem : FullAuditedAggregateRoot
{
    public string Name { get; set; } = string.Empty;
}
```

Entity equality requires the same runtime type and a non-default ID. Two distinct transient instances with default IDs are not equal. The base type does not generate IDs or prevent key mutation.

Audit base classes initialize `CreationTime` to UTC at construction. `Audited*` types expose creator/modifier metadata; `FullAudited*` also expose soft-delete metadata. The aggregate-root variants implement `IMultiTenant`; `FullAuditedEntity<TKey>` does not add a tenant field. These are data fields and interfaces only: this package does not populate actor IDs, update timestamps, filter rows, or intercept deletes.

## Scope Contracts

`IMultiTenant`, `IMemberScoped`, and `ISoftDelete` describe entity fields. `ITenantContext` and `IMemberScopeContext` are read-only scope contracts; this package does not determine how they are populated. Tenant/member selection, trust, and authorization remain host responsibilities.

`DataScopeDimensionRegistry` stores dimension names using ordinal case-insensitive matching. `Dimensions` returns a read-only snapshot; registration is thread-safe and rejects empty names. `DataScopeDimension` defensively copies allowed IDs into a read-only collection:

```csharp
var registry = new DataScopeDimensionRegistry();
registry.Add("store");

var scope = new DataScopeDimension(
    name: "store",
    isAll: false,
    allowedIds: [10, 11],
    currentId: 10);
```

The dimension snapshot and `RoleDataScope.IsAll` are descriptive data only. They do not grant access, parse claims, implement admin bypass, or resolve role assignments.

## Repository Contracts

`IRepository<TEntity,TKey>` and `IReadOnlyRepository<TEntity,TKey>` describe asynchronous query and persistence operations. They do not define a unit of work or transaction policy by themselves. The optional EF Core adapter supplies scope-aware repository behavior and query filtering; use that adapter's guide and configuration for database semantics.

`RoleDataScope` is a tenant-aware record of role, dimension, and scope IDs. Its schema is opt-in via the EF Core mapping extension, which provides a tenant/role index and tenant filtering when used with the adapter. It is not an RBAC implementation or a substitute for permission checks.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Domain`: `AggregateRoot<TKey>` `AmbientMemberScopeContext` `AmbientTenantContext` `AuditedAggregateRoot` `AuditedEntity<TKey>` `CreationAuditedAggregateRoot` `CreationAuditedEntity<TKey>` `DataScopeAccessEvaluator` `DataScopeDimension` `DataScopeDimensionRegistry` `Entity<TKey>` `FullAuditedAggregateRoot` `FullAuditedEntity<TKey>` `IDataScopeDimensionRegistry` `IMemberScopeContext` `IMemberScoped` `IMultiTenant` `IPubSoftSaveParticipant` `IReadOnlyRepository<TEntity, TKey>` `IRepository<TEntity, TKey>` `IRoleDataScopeProvider` `ISoftDelete` `ITenantContext` `PermissionGrantRecord` `PermissionModule` `PermissionRole` `RoleDataScope` `SubjectPermissionRole`
