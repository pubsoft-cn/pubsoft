---
purpose: PubSoft.Outbound 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Outbound

[English](README.md) | **简体中文**

`PubSoft.Contracts` 定义框架无关的 API request/response、`[ApiOperation]`/`[ApiField]` 元数据、命名策略与归一化/校验契约；`PubSoft.Outbound` 增加程序集扫描、alias 查找、参数构建、响应映射与 provider 分发。

```csharp
using PubSoft.Contracts;
using PubSoft.Outbound;

[ApiOperation("records.read", Aliases = new[] { "records.get" }, Verb = ApiHttpVerb.Get)]
public sealed class ReadRecordRequest : IApiRequest<ReadRecordResponse>, IContractValidator
{
    [ApiField("record_id", IsRequired = true, Source = FieldSource.Query)]
    public string? RecordId { get; set; }

    public bool Validate(out string? error)
    {
        error = Guard.Required("record_id", RecordId);
        return error is null;
    }
}
```

## 引擎与分发

`AddOutboundContracts(options => options.Assemblies.Add(...))` 注册 request 所在程序集，注册一个或多个 `IOutboundProvider`，然后解析 `IOutboundEngine`。provider 选择要求恰好一个匹配——无匹配与歧义匹配都在执行前失败。引擎先执行 `IContractNormalizer`、校验必填字段，再对实现了 `IContractValidator` 的类型执行校验，并把显式 profile ID、tenant ID、operation ID 与取消 token 传给 provider。

`IOutboundSdk` 要求显式 tenant ID——SDK 不推断租户 scope、不授权 tenant/profile 选择。宿主仍负责认证、tenant/profile 授权、凭证与密钥存储、HTTP transport、重试/幂等策略、日志/脱敏、限流与 provider 生命周期。

## 请求/响应映射

`ContractRequestBuilder` 默认排除 `FieldSource.Header`，遵循字段顺序、`IgnoreIfNull`/`IgnoreIfDefault`，支持嵌套字段 flatten；重复 wire name 与 flatten 循环直接抛异常。`ContractResponseMapper`/`ContractJsonDeserializer`/`DictionaryResponseMapper` 只映射 `[ApiField]` 声明的字段；转换错误传播而非静默变零值。

`ApiFieldAttribute.IsEncrypted` 仅为描述性元数据——不加密不脱敏。request 契约中不应放密钥，除非 provider 明确安全处理；header-source 排除仅是参数构建器行为，不是通用防泄漏边界。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Outbound`: `ContractJsonDeserializer` `ContractRequestBuilder` `ContractResponseMapper` `DictionaryResponseMapper` `ExternalOutboundSdk` `IOutboundContractRegistry` `JsonValueConverter` `OutboundContractMetadataResolver` `OutboundContractOptions` `OutboundContractRegistry` `OutboundEngine` `OutboundServiceCollectionExtensions` `RequiredFieldValidator`
