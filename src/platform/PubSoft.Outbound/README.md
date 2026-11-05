---
purpose: package-level hosting guide for PubSoft.Outbound
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Outbound

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Contracts` defines framework-neutral API requests/responses, `[ApiOperation]` and `[ApiField]` metadata, naming policies, and normalization/validation contracts. `PubSoft.Outbound` adds assembly scanning, alias lookup, parameter construction, response mapping, and provider dispatch.

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

Register the request assembly with `AddOutboundContracts(options => options.Assemblies.Add(...))`, register one or more `IOutboundProvider` implementations, then resolve `IOutboundEngine`. Provider selection requires exactly one matching provider; no match and ambiguous matches fail before execution. The engine invokes `IContractNormalizer`, validates required fields and then invokes `IContractValidator` when implemented, and passes the explicit profile ID, tenant ID, operation ID, and cancellation token to the provider.

`IOutboundSdk` requires an explicit tenant ID; the SDK does not infer tenant scope or authorize a tenant/profile choice. The host remains responsible for authentication, tenant/profile authorization, credentials and secret storage, HTTP transport, retry/idempotency policy, logging/redaction, rate limits, and provider lifecycle.

## Request/Response Mapping

`ContractRequestBuilder` excludes `FieldSource.Header` by default, honors field order, `IgnoreIfNull` and `IgnoreIfDefault`, and supports flattening nested fields. Duplicate wire names and flatten cycles fail with an exception. `ContractResponseMapper`, `ContractJsonDeserializer`, and `DictionaryResponseMapper` map only fields declared with `[ApiField]`; conversion errors propagate rather than silently becoming zero values.

`ApiFieldAttribute.IsEncrypted` is descriptive metadata only. It does not encrypt or redact values. Do not place secrets in request contracts unless a provider explicitly handles them securely; header-source exclusion is only a parameter-builder behavior, not a general data-loss-prevention boundary.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Outbound`: `ContractJsonDeserializer` `ContractRequestBuilder` `ContractResponseMapper` `DictionaryResponseMapper` `ExternalOutboundSdk` `IOutboundContractRegistry` `JsonValueConverter` `OutboundContractMetadataResolver` `OutboundContractOptions` `OutboundContractRegistry` `OutboundEngine` `OutboundServiceCollectionExtensions` `RequiredFieldValidator`
