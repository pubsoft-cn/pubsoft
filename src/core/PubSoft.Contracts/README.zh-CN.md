# PubSoft.Contracts

[English](README.md) | **简体中文**

`PubSoft.Contracts` 提供零依赖的 request/response 配对、API 操作与字段元数据、命名策略、归一化/校验接口、通用 Guard 以及通用 outbound provider/engine/SDK 契约。

```csharp
using PubSoft.Contracts;

[ApiOperation("records.read", Verb = ApiHttpVerb.Get)]
public sealed class ReadRecordRequest : IApiRequest<ReadRecordResponse>, IContractValidator
{
    [ApiField("record_id", IsRequired = true, Source = FieldSource.Query)]
    public string? RecordId { get; init; }

    public bool Validate(out string? error)
    {
        error = Guard.Required("record_id", RecordId);
        return error is null;
    }
}
```

## 语义与边界

元数据标注本身不执行 transport 序列化、路由、校验、脱敏或加密——`ApiFieldAttribute.IsEncrypted` 仅为描述性元数据，不得视为对值的保护。`IContractValidator`/`IContractNormalizer` 为 opt-in 契约，仅在应用显式调用或使用显式执行的适配器时生效。

`IOutboundProvider`/`IOutboundEngine`/`IOutboundSdk` 定义宿主中立的集成边界；provider 选择、凭证、tenant/profile 授权、重试、日志、HTTP transport 与响应映射归 outbound 适配器与宿主配置。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Contracts`: `ApiContractMetadata` `ApiFieldAttribute` `ApiHttpVerb` `ApiOperationAttribute` `CamelCaseNamingPolicy` `EmitBehavior` `FieldSource` `Guard` `IApiRequest` `IApiRequest<TResponse>` `IContractNormalizer` `IContractValidator` `INamingPolicy` `IOutboundEngine` `IOutboundProvider` `IOutboundSdk` `OutboundProviderContext` `PascalCaseNamingPolicy` `SnakeCaseNamingPolicy`
