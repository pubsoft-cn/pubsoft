---
purpose: package-level hosting guide for PubSoft.Monitoring.OpenTelemetry
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-11-29
---

# PubSoft.Monitoring.OpenTelemetry

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/platform/PubSoft.Monitoring.OpenTelemetry/README.zh-CN.md)

Opinionated OpenTelemetry adoption glue for PubSoft monitoring signals. The package wires the provider-neutral `PubSoft.Monitoring` meter into the OpenTelemetry SDK, optionally adds ASP.NET Core/HTTP client trace instrumentation, and exports through OTLP only when an endpoint is explicitly configured.

## Scope and Guarantees

- `ServiceName` is required and validated eagerly; invalid endpoints, duplicate names, and unbounded source lists fail registration before a provider is created.
- `PubSoft.Monitoring`'s meter is always included. Additional meters/activity sources are explicit, deduplicated, and capped at 64 each.
- ASP.NET Core and outbound HTTP trace instrumentation are opt-in; they are off by default to avoid surprise cardinality and telemetry volume.
- OTLP export is opt-in through an absolute `http`/`https` `OtlpEndpoint`. No exporter runs when it is null.
- This package does not mark itself AOT-compatible. The upstream instrumentation/exporter dependency graph must be validated in each trimmed or Native AOT host before making that claim.

## Setup

```csharp
using PubSoft.Monitoring.OpenTelemetry;

services.AddPubSoftOpenTelemetry(options =>
{
    options.ServiceName = "demo-app";
    options.ServiceVersion = "1.0.0";
    options.OtlpEndpoint = new Uri("http://localhost:4317");
    options.IncludeAspNetCoreTraceInstrumentation = true;
    options.IncludeHttpClientTraceInstrumentation = true;
});
```

## Options

| Option | Default | Meaning |
|---|---|---|
| `ServiceName` | required | OpenTelemetry resource service name; max 200 chars, no control characters |
| `ServiceVersion` | `null` | Optional resource service version |
| `OtlpEndpoint` | `null` | Explicit OTLP export opt-in; must be absolute HTTP(S) |
| `IncludeAspNetCoreTraceInstrumentation` | `false` | Adds inbound ASP.NET Core trace instrumentation |
| `IncludeHttpClientTraceInstrumentation` | `false` | Adds outbound HTTP client trace instrumentation |
| `AdditionalMeters` | empty | Up to 64 unique extra meter names |
| `AdditionalActivitySources` | empty | Up to 64 unique extra activity source names |

## Residual Host Responsibilities

The package does not operate the collector, choose tenant/environment-specific egress policy, protect credentials, tune sampling, or decide which high-cardinality attributes are acceptable. It does not replace `PubSoft.Tracing`'s jsonl exporter; hosts may run either or both intentionally. Logging correlation and log export remain separate host concerns.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Monitoring.OpenTelemetry`: `OpenTelemetryServiceCollectionExtensions` `PubSoftOpenTelemetryOptions`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftOpenTelemetry` |

**(options & records)**

`AdditionalActivitySources`, `AdditionalMeters`, `IncludeAspNetCoreTraceInstrumentation`, `IncludeHttpClientTraceInstrumentation`, `OtlpEndpoint`, `ServiceName`, `ServiceVersion`

<!-- pubsoft:api-surface:end -->
