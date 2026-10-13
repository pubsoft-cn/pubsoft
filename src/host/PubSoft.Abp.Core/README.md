# PubSoft.Abp.Core

**English** | [简体中文](README.zh-CN.md)

Optional Volo.Abp adapter: registers the `SnowflakeIdGenerator` singleton inside an ABP module.

## Registration

```csharp
[DependsOn(typeof(PubSoftCoreModule))]
public sealed class HostModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.Configure<PubSoftSnowflakeOptions>(o => o.WorkerId = 7);
}
```

`WorkerId` must be explicitly configured in range 0–31 and unique per running instance — there is no default, so two instances can never silently share an ID space. Invalid or missing values fail options validation at resolution time.

## Boundaries

Non-ABP hosts should use the generic `PubSoft.Domain`/`PubSoft.Core` path instead; this package exists only for ABP module wiring. It registers nothing else — no repository conventions, no implicit UoW, no MVC behavior.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Abp.Core`: `PubSoftCoreModule` `PubSoftCoreServiceCollectionExtensions` `PubSoftSnowflakeOptions`
