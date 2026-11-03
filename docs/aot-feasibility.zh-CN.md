# Native AOT 可行性分析（2026-10-12，2026-10-12 落地）

[English](aot-feasibility.md) | **简体中文**

**结论：** 全仓库 Native AOT 不可行（EF Core / ASP.NET Core MVC 上游不支持）。**纯托管叶子包已标注 `IsAotCompatible` 并通过真实验证**：`tests/PubSoft.AotSmoke`（`PublishAot=true` console，引用各叶子包）`dotnet publish -r linux-x64` **零 IL2xxx/IL3xxx 警告**，原生二进制运行通过。入口：`scripts/verify-aot-consumer.sh`（已进 CI；无原生工具链时优雅跳过）。

## 依赖侧硬约束（不可绕行）

| 依赖 | AOT 状态 | 影响范围 |
|---|---|---|
| EF Core 10 | **不支持 Native AOT**（运行时模型构建重度依赖反射；compiled model 只加速启动不改本质） | `EntityFrameworkCore`、`Messaging.Outbox`、`Scheduling.EntityFrameworkCore`、所有 EF 持久化路径 |
| ASP.NET Core MVC | 不支持（controller 模型绑定/筛选器是反射驱动）；Minimal API 部分支持 | `AspNetCore`（含 MVC filter） |
| StackExchange.Redis | 未承诺 AOT；需逐版本验证 | `Caching.Redis`、`Security.Redis` |

## 包分级矩阵

### A 级：今天基本干净（无反射热点，可直接标记）

`Core`、`Domain`、`Contracts`、`Caching`（新加的 `InProcessDistributedLock` 为纯并发原语）、`Security`、`Cryptography`（BouncyCastle 为纯托管实现，低反射风险）、`Logging`、`Notifications`、`MultiTenancy`、`Scheduling`、`Events`（`MakeGenericType` 为纯元数据操作、AOT 安全；`GetServices(Type)` 走 MS.DI 的 AOT 标注路径）、`Tokens`（手动 `JsonElement` 解析，无反射序列化）、`Testing`、`Monitoring`、`Resilience`（`Resilience.Http` 仅标准 `HttpClient`/`DelegatingHandler`）、`Authentication`（快照+resolver，纯类型）、`Outbound`（见 B 级说明）、`Identity`（纯领域模型）。

### B 级：可行但需代码调整

| 包 | 问题 | 处理 |
|---|---|---|
| `Mapping` | `Expression.Compile()` + 运行时 `GetProperties` | AOT 下 Compile 自动走解释器（.NET 8+，功能正常、性能下降）；`TSource`/`TDestination` 泛型参数需 `[DynamicallyAccessedMembers(PublicProperties \| ParameterlessConstructors)]` 防裁剪 |
| `Outbound` | `GetProperties`/`GetCustomAttribute<ApiFieldAttribute>` 于任意 request/response 类型；`JsonSerializer.Deserialize(value, Type)` 运行时类型；`List<>` `MakeGenericType` + `Activator.CreateInstance` | 成员反射需 `DynamicallyAccessedMembers` 标注；运行时 STJ 需改为经 `JsonTypeInfoResolver` 的 options 或标注 `RequiresUnreferencedCode`；值类型 elementType 的 `MakeGenericType` 在 AOT 下可能缺实例化，需测试或标注 |
| `Messaging` | `SerializeToElement(payload, options)` | 已由宿主传 `JsonSerializerOptions`——文档要求宿主提供 source-gen `JsonSerializerContext` 即可 |
| `Messaging.Outbox` | `JsonSerializer.Serialize(@event, eventType)` 运行时类型 + EF 依赖 | EF 已判不支持；序列化点若日后拆出非 EF 契约包需同样走 resolver |
| `Auditing` | `JsonSerializer.SerializeToElement(value, type)` 运行时类型 | 同上 |
| `Caching.Redis`/`Security.Redis` | `JsonSerializer.<T>` 经宿主 options（OK）；StackExchange.Redis 本体待验证 | 序列化侧可行；Redis 客户端需单独发布验证 |
| `AspNetCore` | 中间件本身可在 AOT 宿主运行；`AuthorizeFilter`/MVC convention 依赖 MVC | 可将纯中间件能力与 MVC 依赖拆分子集，或整包声明非 AOT |
| `Authentication.Jwt.AspNetCore` | JwtBearer/`Microsoft.IdentityModel` 近期版本基本 AOT 兼容 | 需端到端发布验证；bearer 本身无 MVC 依赖 |

### C 级：不支持（依赖决定）

`EntityFrameworkCore`、`Scheduling.EntityFrameworkCore`、`Messaging.Outbox`、`Auditing.Outbox`（EF sink 链路）。

## 当前已具备的 AOT 友好设计（保持）

- 所有序列化点都接受宿主 `JsonSerializerOptions`——宿主注入 source-gen `JsonSerializerContext` 即可满足 AOT；**不要**引入"默认 options + 运行时类型"的新调用点。
- 无 `Type.GetType` 字符串解析、无程序集扫描注册——fail-closed dispatcher/显式注册与 AOT 天然兼容。
- DI 全部显式 `Add*` 注册，MS.DI 已标注 AOT/trim 兼容。

## 2026-10-12 落地情况

**已标注 `IsAotCompatible`：**`Core`、`Domain`、`Contracts`、`Caching`、`Security`、`Cryptography`、`Logging`、`Notifications`、`MultiTenancy`、`Scheduling`、`Scheduling.Http`、`Events`、`Tokens`、`Testing`、`Monitoring`、`Monitoring.Scheduling`、`Resilience`、`Resilience.Http`、`Authentication`、`Authentication.Jwt`、`Authentication.OAuthProviders`、`Identity`、`Messaging`、`Authorization`、`Mapping`、`Outbound`、`Auditing`。

**采取的修复模式**：
- 宿主传 `JsonSerializerOptions` 的 STJ 调用点 → `UnconditionalSuppressMessage` + 契约文档（`Messaging` envelope/dispatcher）
- 库内固定类型 STJ → source-gen `JsonSerializerContext`（`Scheduling.Http`、`Monitoring.Scheduling`）
- 运行时类型 STJ / 反射契约映射 → `RequiresUnreferencedCode`/`RequiresDynamicCode` 标注到公共边界（`Outbound` 引擎链、`Auditing` 值序列化器——消费方 EF/AspNetCore 本就非 AOT）
- 成员反射 → `DynamicallyAccessedMembers` 泛型参数标注（`Mapping`、`Outbound` 元数据解析、DI 泛型注册扩展）
- 按名查找 `Expression.Property` → 语义等价的免反射写法（`Mapping` 的 `Nullable<T>` 路径）
- 运行时事件类型的 DI 解析 → 抑制 + 文档化"delegate handler 为 AOT 路径"（`Events`）

**验证发现**：`dotnet publish` 在包变更后可能跳过 native 链接（增量发布复用陈旧二进制）——AOT smoke 必须清理 publish 输出或全新 obj/bin 后运行，勿信任"上一次二进制"。

**待验证后定级**：`Caching.Redis`、`Security.Redis`（StackExchange.Redis 上游未承诺）、`Authentication.Jwt.AspNetCore`（JwtBearer/IdentityModel）。

## 对宿主生态的含义

ASP.NET Core MVC / EF Core / ABP 宿主——**业务服务永远走 JIT**（上游均不发布 AOT 兼容承诺）。AOT 兼容的价值在 PubSoft 作为通用库被其他场景消费：CLI、agent、边缘函数。因此 AOT 投入应限定在叶子包标注这一低成本区间，不为 EF/ASP.NET 适配层做任何 AOT 妥协。
