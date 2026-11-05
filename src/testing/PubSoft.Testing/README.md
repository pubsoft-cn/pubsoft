---
purpose: package-level hosting guide for PubSoft.Testing
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Testing

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/testing/PubSoft.Testing/README.zh-CN.md)

Deterministic test doubles for PubSoft contracts — plain classes with no test-framework dependency, usable from xUnit/NUnit/MSTest or a console harness.

## Provided Fakes

```csharp
var tenant = new FixedTenantContext(7);                    // ITenantContext
var member = new FixedMemberScopeContext(11);              // IMemberScopeContext
var time   = new TestTimeProvider(DateTimeOffset.UtcNow);  // TimeProvider
time.Advance(TimeSpan.FromHours(2));                       // deterministic expiry tests
var saver  = new DelegateSaveParticipant(ct => mySave(ct));// IPubSoftSaveParticipant
```

| Fake | Implements | Use for |
|---|---|---|
| `FixedTenantContext` | `ITenantContext` | repository/filter/evaluator tests |
| `FixedMemberScopeContext` | `IMemberScopeContext` | member-scope filtering tests |
| `TestTimeProvider` | `TimeProvider` | expiry, retry-delay, retention, scheduling tests |
| `DelegateSaveParticipant` | `IPubSoftSaveParticipant` | request-finalization pipeline tests without a DbContext |

## Boundaries

These fakes are intentionally minimal — they do **not** emulate AsyncLocal ambient behavior. For tests of `Change()` scope semantics use `AmbientTenantContext`/`AmbientMemberScopeContext` directly.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Testing`: `DelegateSaveParticipant` `FixedMemberScopeContext` `FixedTenantContext` `TestTimeProvider`
