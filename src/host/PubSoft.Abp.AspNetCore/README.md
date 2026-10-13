# PubSoft.Abp.AspNetCore

**English** | [简体中文](README.zh-CN.md)

Optional Volo.Abp adapter: registers the scoped `IPrincipalSnapshotAccessor` from `PubSoft.AspNetCore` inside an ABP module.

## Registration

```csharp
[DependsOn(typeof(PubSoftAbpAspNetCoreModule))]
public sealed class HostModule : AbpModule { }
```

The accessor snapshots `HttpContext.User` verbatim — authenticated flag, ordered claims with original type/value/issuer — and returns `null` outside a request. It does not interpret claims, resolve tenants or roles, or configure authorization. Legacy implicit unit-of-work, MVC conventions, and static-web-application startup extensions are deliberately not registered.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Abp.AspNetCore`: `PubSoftAbpAspNetCoreModule` `PubSoftAbpAspNetCoreServiceCollectionExtensions`
