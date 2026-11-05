# PubSoft 文档

[English](README.md) | **简体中文**

`docs/` 收录**跨包**的框架级设计文档；各包自身的宿主指南（注册、配置、边界）已就近放在对应包的 `src/*/PubSoft.*/README.md`（同时随 NuGet 包发布，目前以英文提供）。

## 框架级文档

- [设计原则](design-principles.zh-CN.md) — fail-closed、显式 opt-in、有界、反转依赖、AOT 策略、ambient scope 规则等贯穿全部包的规范
- [打包与发布工程](packaging.zh-CN.md) — 版本管理、Public API 基线、依赖 allowlist、隔离 consumer 验证、AOT smoke、发布门槛
- [Native AOT 可行性](aot-feasibility.zh-CN.md) — 上游支持面、已标注包清单、落地验证结果
- [安全模型](security-cryptography.zh-CN.md) — Security/Cryptography/AspNetCore 包边界、AEAD 包封格式、nonce/HMAC 语义
- [安全与信任边界分析](security-boundary-analysis.zh-CN.md) — 外部审计：控制/责任矩阵、API 清单、优先级发现 R1-R6、宿主部署清单
- [Permission Client Scope](permission-client-scope.zh-CN.md) — `ClientCode` 权限命名空间语义（跨 Authorization/EF/ASP.NET 层）

## 包文档索引

| 层 | 包 → 指南 |
|---|---|
| 基础 | [Core](../src/core/PubSoft.Core/README.md) · [Domain](../src/core/PubSoft.Domain/README.md) · [Contracts](../src/core/PubSoft.Contracts/README.md) |
| 身份/认证 | [Authentication](../src/security/PubSoft.Authentication/README.md) · [Authentication.Jwt](../src/security/PubSoft.Authentication.Jwt/README.md)（含 [Jwt.AspNetCore](../src/security/PubSoft.Authentication.Jwt.AspNetCore/README.md)）· [OAuthProviders](../src/security/PubSoft.Authentication.OAuthProviders/README.md) · [Identity](../src/core/PubSoft.Identity/README.md) · [Authorization](../src/security/PubSoft.Authorization/README.md) · [MultiTenancy](../src/core/PubSoft.MultiTenancy/README.md) |
| 数据 | [EntityFrameworkCore](../src/data/PubSoft.EntityFrameworkCore/README.md) · [Mapping](../src/core/PubSoft.Mapping/README.md) |
| 消息/审计 | [Messaging](../src/messaging/PubSoft.Messaging/README.md) · [Messaging.Outbox](../src/messaging/PubSoft.Messaging.Outbox/README.md) · [Modularity](../src/platform/PubSoft.Modularity/README.md) · [Auditing](../src/data/PubSoft.Auditing/README.md) · [Auditing.Outbox](../src/data/PubSoft.Auditing.Outbox/README.md) · [Events](../src/core/PubSoft.Events/README.md) |
| 缓存/安全 | [Caching](../src/platform/PubSoft.Caching/README.md) · [Caching.Redis](../src/platform/PubSoft.Caching.Redis/README.md) · [Security](../src/security/PubSoft.Security/README.md) · [Security.Redis](../src/security/PubSoft.Security.Redis/README.md) · [Cryptography](../src/core/PubSoft.Cryptography/README.md) |
| 运行时 | [AspNetCore](../src/host/PubSoft.AspNetCore/README.md) · [Resilience](../src/core/PubSoft.Resilience/README.md) · [Resilience.Http](../src/platform/PubSoft.Resilience.Http/README.md) · [Scheduling](../src/platform/PubSoft.Scheduling/README.md)（[EF](../src/platform/PubSoft.Scheduling.EntityFrameworkCore/README.md) · [Http](../src/platform/PubSoft.Scheduling.Http/README.md)）· [Monitoring](../src/platform/PubSoft.Monitoring/README.md)（[Scheduling](../src/platform/PubSoft.Monitoring.Scheduling/README.md)）· [Logging](../src/core/PubSoft.Logging/README.md) · [Notifications](../src/platform/PubSoft.Notifications/README.md) · [Tokens](../src/platform/PubSoft.Tokens/README.md) · [Outbound](../src/platform/PubSoft.Outbound/README.md) |
| 测试 | [Testing](../src/testing/PubSoft.Testing/README.md) |

迁移过程文档（蓝图/状态/差距分析/发布清单）在 `internal/`（gitignore，不随开源发布）。
