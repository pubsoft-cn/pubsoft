---
purpose: Activity span 导出器——写 spans-{service}-*.jsonl，供 trace/CDC 关联
audience: 集成宿主开发者、运维、评审者
owner: PubSoft 维护组
last-reviewed: 2026-11-20
---

# PubSoft.Tracing

有界 `ActivityListener` 导出器：每个结束的 W3C activity 落成一行 span doc
——`{ts, durMs, tid, sid, psid, name, kind, status, err?, svc, svc_ver?, tags?}`——
写入 `spans-{service}-{yyyy-MM-dd}.jsonl` 或宿主提供的 output。

## 范围与保障

- **join 键是核心**：`tid`/`sid`/`psid` 原样取自 activity——span doc 可与网关 trace id、
  `PubSoftAuditLogs.trace_id`、CDC 事件直接关联。
- **有界不阻塞热路径**：队列（`Capacity` 默认 10_000）满时丢弃 span 并计入
  `DroppedCount`——tracing 不会拖慢请求。
- **选项 fail-closed**：`ServiceName` 必须提供，`Directory`/`Output` 必须二选一，
  注册期即校验。
- 默认采样 `AllDataAndRecorded`——会给所有 activity 置 `Recorded` 标志（出站
  traceparent 即 `-01`）。要降量请覆盖 `Sampling`。

## 接入

```csharp
services.AddPubSoftTracing(o =>
{
    o.ServiceName = "demo-app";          // -> spans-demo-app-yyyy-MM-dd.jsonl
    o.Directory = "/var/log/pubsoft";    // 或 o.Output = 自定义字节写出
});
```

listener 随宿主启动开始（hosted service 提前解析 exporter），容器在关闭时
drain 并释放。

## 选项

| 选项 | 默认 | 含义 |
|---|---|---|
| `ServiceName` | 必填 | `svc` 字段 + 文件名段（须文件名安全） |
| `ServiceVersion` | null | `svc_ver` 字段 |
| `Directory` | 三选一必填 | `spans-{svc}-yyyy-MM-dd.jsonl` 目录，UTC 日滚动 |
| `Output` | 三选一必填 | 自定义批量写出（jsonl 字节），替代文件 |
| `DocOutput` | 三选一必填 | 结构化 `SpanDoc` 批次——自定义 doc 形状，免字节往返 |
| `Capacity` | 10_000 | 有界 span 队列；超限丢弃并计数 |
| `BatchSize` | 64 | 刷盘批量 |
| `ShouldListenTo` | 全部 source | 收窄以剔除噪声源 |
| `Sampling` | AllDataAndRecorded | listener 采样决策 |
| `IncludeBaggage` | false | 把 baggage 并入 `tags`（调用方可控——有 PII 风险） |

## 宿主残余责任

- 本包只产 span doc——不含采样 UI、上游 trace 存储、除 UTC 日滚动外的保留/轮转。
  文件生命周期归宿主。
- `Activity.Current` 是规范 trace 上下文；宿主若在请求中重新注入不同 traceparent，
  span 父子关系跟随 activity，不跟随 header。
- Baggage 与任意 tag 值受调用方影响；开启 `IncludeBaggage` 前先审查上游可能传入的内容。

## Public API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**(options & records)**

`BatchSize`, `Capacity`, `Directory`, `DroppedCount`, `IncludeBaggage`, `Output`, `Sampling`, `ServiceName`, `ServiceVersion`, `ShouldListenTo`

<!-- pubsoft:api-surface:end -->
