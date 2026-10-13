# PubSoft.MultiTenancy

**English** | [简体中文](README.zh-CN.md)


`PubSoft.MultiTenancy` provides a framework-independent tenant model and management DTO/service contracts. It depends on `PubSoft.Domain`; it does not provide an HTTP tenant resolver, current-tenant accessor, administrator authorization, or an application-service implementation.

## Tenant Model

Create a tenant with an application-assigned ID and a stable code:

```csharp
using PubSoft.MultiTenancy;

var tenant = new Tenant(
    id: 42,
    code: "store-a",
    name: "Store A",
    displayName: "Store Alpha");
```

`Code` and `Name` are required, trimmed, and limited to 64 and 100 characters respectively. `Code` has no update method, so changing a tenant code requires an explicit host-level migration policy. `DisplayName` is trimmed, limited to 100 characters, and defaults to `Name` when omitted or blank. `Description` is optional, trimmed, and limited to 200 characters. `CreationTime` is initialized in UTC. `Update` changes the name, display name, and description; `SetEnabled` changes the enabled flag.

The optional `PubSoft.EntityFrameworkCore` tenant mapping is separate from this package. It applies the tenant schema mapping, including a unique code constraint; hosts still choose and configure their database provider and migrations.

## Management Contracts

`TenantDto`, `CreateTenantInput`, `UpdateTenantInput`, and `ITenantAppService` define data shapes and asynchronous operations only. The package does not implement persistence, tenant listing visibility, deletion behavior, or authorization. A host must enforce who may create, update, list, or delete tenants and define any tenant lifecycle rules.

This model is not a trusted tenant-selection mechanism. Do not treat a request header, route value, or user-supplied tenant ID as authorization to switch scope. Resolving the active tenant and authorizing tenant changes belong to an explicitly configured host identity/authorization boundary.

## Ambient Scope Contexts

`PubSoft.Domain` provides `AmbientTenantContext` (an `ITenantContext`) and `AmbientMemberScopeContext` (an `IMemberScopeContext`) backed by async-local storage. Register as singletons — ambient state is per async control flow, so instance lifetime does not affect isolation:

```csharp
services.AddSingleton<ITenantContext, AmbientTenantContext>();
services.AddSingleton<IMemberScopeContext, AmbientMemberScopeContext>();
```

A request/job adapter calls `Change(tenantId)` once from a trusted source; the returned scope restores the previous value on dispose. Scoping an endpoint to a tenant must wrap the endpoint invocation (`using (ctx.Change(id)) await next()`): ambient values set inside an async callee never flow back to the caller frame. A member scope with `IsMemberScoped = true` but no `MemberId` fails closed (queries match nothing) rather than widening results. Neither type validates the authority of the value being set — who may change scope is a host authorization decision.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.MultiTenancy`: `CreateTenantInput` `ITenantAppService` `Tenant` `TenantDto` `UpdateTenantInput`
