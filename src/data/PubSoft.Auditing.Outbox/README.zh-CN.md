# PubSoft.Auditing.Outbox

[English](README.md) | **简体中文**

将 finalized `AuditRequestSnapshot` 经持久化 Outbox 转发，而非直接写审计表：

```csharp
services.AddPubSoftAuditOutbox<AppDbContext>();
```

## 语义

- 版本化 snapshot payload 与请求业务事务共用同一 scoped `DbContext`——审计与业务写入原子提交
- 到审计存储的投递为 at-least-once；宿主须运行 `IOutboxDispatcher` + 幂等消费者持久化 snapshot
- 该 sink 不绑定此前已提交的事务——只覆盖当前 scope 内保存的工作

## 边界

dispatcher 注册、payload 隐私/保留期审查、消费者幂等归宿主。直连 EF 审计 sink（`PubSoft.EntityFrameworkCore`）可独立使用——每条管线二选一。
