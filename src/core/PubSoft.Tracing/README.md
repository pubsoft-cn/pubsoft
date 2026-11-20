---
purpose: Activity span exporter writing spans-{service}-*.jsonl for trace/CDC correlation
audience: integrating host developers, operators, reviewers
owner: PubSoft maintainers
last-reviewed: 2026-11-20
---

# PubSoft.Tracing

Bounded `ActivityListener` exporter: every stopped W3C activity becomes one jsonl span
document — `{ts, durMs, tid, sid, psid, name, kind, status, err?, svc, svc_ver?, tags?}` —
written to `spans-{service}-{yyyy-MM-dd}.jsonl` or a host-provided output.

## Scope and Guarantees

- **Join keys are the point**: `tid`/`sid`/`psid` are emitted verbatim from the activity
  so span docs join with gateway trace ids, `PubSoftAuditLogs.trace_id`, and CDC events.
- **Bounded, never hot-path blocking**: the queue (`Capacity`, default 10_000) drops
  spans under pressure and counts them on `DroppedCount` — tracing cannot stall requests.
- **Fail-closed options**: `ServiceName` plus exactly one of `Directory`/`Output` is
  required and validated at registration.
- The exporter samples `AllDataAndRecorded` by default — that sets the `Recorded` flag
  on every activity (and `-01` on outbound traceparents). Override `Sampling` to thin.

## Setup

```csharp
services.AddPubSoftTracing(o =>
{
    o.ServiceName = "demo-app";          // -> spans-demo-app-yyyy-MM-dd.jsonl
    o.Directory = "/var/log/pubsoft";    // or o.Output = custom byte writer
});
```

The listener starts when the host starts (a hosted service resolves the exporter
eagerly) and the container drains and disposes it at shutdown.

## Options

| Option | Default | Meaning |
|---|---|---|
| `ServiceName` | required | `svc` field + file-name segment (file-name safe) |
| `ServiceVersion` | null | `svc_ver` field |
| `Directory` | one of three | `spans-{svc}-yyyy-MM-dd.jsonl` dir, UTC-day rollover |
| `Output` | one of three | custom batch writer (jsonl bytes) instead of a file |
| `DocOutput` | one of three | structured `SpanDoc` batch - shape your own docs, no byte round trip |
| `Capacity` | 10_000 | bounded span queue; excess drops + counts |
| `BatchSize` | 64 | flush batch size |
| `ShouldListenTo` | all sources | narrow to keep noisy sources out |
| `Sampling` | AllDataAndRecorded | listener sampling decision |
| `IncludeBaggage` | false | merge baggage into `tags` (caller-controlled — PII risk) |

## Residual Host Responsibilities

- This package emits span docs only — no tail sampling UI, no upstream trace store,
  no retention/rotation beyond UTC-day file rollover. File lifecycle is the host's.
- `Activity.Current` is the canonical trace context; if the host re-injects a different
  traceparent mid-request, span parentage follows the activity, not headers.
- Baggage and arbitrary tag values are caller-influenced; enable `IncludeBaggage`
  only after reviewing what upstreams may send.

## Public API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftTracing` |

**(options & records)**

`Baggage`, `BatchSize`, `Capacity`, `Directory`, `DocOutput`, `DurationMilliseconds`, `Error`, `IncludeBaggage`, `Output`, `ParentSpanId`, `Sampling`, `ServiceName`, `ServiceVersion`, `ShouldListenTo`, `SpanId`, `StartedUtc`, `Status`, `TraceId`

<!-- pubsoft:api-surface:end -->
