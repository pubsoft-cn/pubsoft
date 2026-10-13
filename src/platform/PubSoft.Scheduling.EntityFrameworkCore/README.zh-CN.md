# PubSoft.Scheduling.EntityFrameworkCore

[English](README.md) | **简体中文**

`PubSoft.Scheduling` 的 EF Core 持久化 `IStateStore`：

```csharp
services.AddPubSoftEfCoreSchedulerStateStore<AppDbContext>();
```

## 语义

- 每次操作自建 async DI scope + `DbContext`；state 行按 ambient `ITenantContext` 隔离
- 逻辑 job key 唯一；custom state 支持过期与显式删除；last result 保留 7 天
- `PubSoft.Scheduling` 默认 `MemoryStateStore` 为单进程易失——换持久化时注册本包

## 边界

本 store 提供持久化单写者语义。它**不**提供 job lease、集群互斥或 misfire 恢复——多节点调度需外部协调（如 `IDistributedLock`）。见 [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.zh-CN.md)。
