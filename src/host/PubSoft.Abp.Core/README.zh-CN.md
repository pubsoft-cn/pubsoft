# PubSoft.Abp.Core

[English](README.md) | **简体中文**

可选 Volo.Abp 适配器：在 ABP 模块内注册 `SnowflakeIdGenerator` 单例。

## 注册

```csharp
[DependsOn(typeof(PubSoftCoreModule))]
public sealed class HostModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.Configure<PubSoftSnowflakeOptions>(o => o.WorkerId = 7);
}
```

`WorkerId` 必须显式配置且范围 0–31、每运行实例唯一——没有默认值，两个实例不可能静默共享同一 ID 空间。无效或缺失值在解析时触发 options 校验失败。

## 边界

非 ABP 宿主请走通用 `PubSoft.Domain`/`PubSoft.Core` 路径；本包仅提供 ABP 模块接线。不注册其他东西——无仓储约定、无隐式 UoW、无 MVC 行为。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Abp.Core`: `PubSoftCoreModule` `PubSoftCoreServiceCollectionExtensions` `PubSoftSnowflakeOptions`
