---
purpose: PubSoft.Monitoring.OpenTelemetry 包级宿主接入指南
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-11-29
---

# PubSoft.Monitoring.OpenTelemetry

[English](https://github.com/pubsoft-cn/pubsoft/blob/main/src/platform/PubSoft.Monitoring.OpenTelemetry/README.md) | **简体中文**

面向 PubSoft 监控信号的 OpenTelemetry 收编接入包。它把 provider-neutral 的 `PubSoft.Monitoring` meter 接入 OpenTelemetry SDK，可选加入 ASP.NET Core/HTTP client trace instrumentation，并且只有显式配置 endpoint 时才启用 OTLP 导出。

## 范围与保障

- `ServiceName` 必填并在注册时立即校验；非法 endpoint、重复名称与超界 source 列表会在 provider 创建前失败。
- 始终包含 `PubSoft.Monitoring` meter。额外 meter/activity source 必须显式声明、去重，且受包级上限约束。
- ASP.NET Core 与出站 HTTP trace instrumentation 均为 opt-in，默认关闭以避免意外基数与遥测量。
- OTLP 导出通过绝对 `http`/`https` `OtlpEndpoint` 显式开启；为 null 时不运行 exporter。
- 本包不声明 AOT 兼容。裁剪或 Native AOT 宿主必须自行验证上游 instrumentation/exporter 依赖图后才能作此承诺。

## 接入

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

## 选项

| 选项 | 默认值 | 含义 |
|---|---|---|
| `ServiceName` | 必填 | OpenTelemetry resource service name；最多 200 字符且不含控制字符 |
| `ServiceVersion` | `null` | 可选 resource service version |
| `OtlpEndpoint` | `null` | 显式 OTLP 导出 opt-in；必须是绝对 HTTP(S) URI |
| `IncludeAspNetCoreTraceInstrumentation` | `false` | 加入入站 ASP.NET Core trace instrumentation |
| `IncludeHttpClientTraceInstrumentation` | `false` | 加入出站 HTTP client trace instrumentation |
| `AdditionalMeters` | 空 | 有界数量的去重额外 meter 名 |
| `AdditionalActivitySources` | 空 | 有界数量的去重额外 activity source 名 |

## 宿主残余责任

本包不运维 collector、不决定租户/环境 egress 策略、不保管凭证、不调 sampling，也不判断哪些高基数字段可接受。它不替代 `PubSoft.Tracing` 的 jsonl exporter；宿主可明确选择其一或同时运行。日志关联与日志导出仍是独立宿主事项。
