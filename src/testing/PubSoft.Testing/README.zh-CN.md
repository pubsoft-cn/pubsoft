# PubSoft.Testing

[English](README.md) | **简体中文**

PubSoft 契约的确定性测试替身——纯类库、无测试框架依赖，xUnit/NUnit/MSTest 或 console harness 均可用。

## 提供的 Fake

```csharp
var tenant = new FixedTenantContext(7);                    // ITenantContext
var member = new FixedMemberScopeContext(11);              // IMemberScopeContext
var time   = new TestTimeProvider(DateTimeOffset.UtcNow);  // TimeProvider
time.Advance(TimeSpan.FromHours(2));                       // 确定性过期测试
var saver  = new DelegateSaveParticipant(ct => mySave(ct));// IPubSoftSaveParticipant
```

| Fake | 实现 | 用于 |
|---|---|---|
| `FixedTenantContext` | `ITenantContext` | 仓储/过滤器/evaluator 测试 |
| `FixedMemberScopeContext` | `IMemberScopeContext` | member-scope 过滤测试 |
| `TestTimeProvider` | `TimeProvider` | 过期、重试延迟、保留期、调度测试 |
| `DelegateSaveParticipant` | `IPubSoftSaveParticipant` | 无 DbContext 测请求收尾管线 |

## 边界

这些 fake 刻意最小——**不**模拟 AsyncLocal ambient 行为。测 `Change()` scope 语义请直接用 `AmbientTenantContext`/`AmbientMemberScopeContext`。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Testing`: `DelegateSaveParticipant` `FixedMemberScopeContext` `FixedTenantContext` `TestTimeProvider`
