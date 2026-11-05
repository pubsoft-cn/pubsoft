---
purpose: package-level hosting guide for PubSoft.Contracts
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Contracts

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Contracts` contains dependency-free request/response pairing, API operation and field metadata, naming policies, normalization/validation interfaces, reusable guards, and generic outbound provider/engine/SDK contracts.

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

## Semantics and Boundaries

The metadata attributes do not perform transport serialization, routing, validation, redaction, or encryption by themselves. In particular, `ApiFieldAttribute.IsEncrypted` is descriptive metadata only; callers must not treat it as protection for a value. `IContractValidator` and `IContractNormalizer` are opt-in contracts, and their behavior runs only when an application invokes them or uses an adapter that explicitly does so.

`IOutboundProvider`, `IOutboundEngine`, and `IOutboundSdk` define host-neutral integration boundaries. Provider selection, credentials, tenant/profile authorization, retries, logging, HTTP transport, and response mapping belong to the outbound adapter and host configuration.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Contracts`: `ApiContractMetadata` `ApiFieldAttribute` `ApiHttpVerb` `ApiOperationAttribute` `CamelCaseNamingPolicy` `EmitBehavior` `FieldSource` `Guard` `IApiRequest` `IApiRequest<TResponse>` `IContractNormalizer` `IContractValidator` `INamingPolicy` `IOutboundEngine` `IOutboundProvider` `IOutboundSdk` `OutboundProviderContext` `PascalCaseNamingPolicy` `SnakeCaseNamingPolicy`
