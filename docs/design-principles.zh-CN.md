---
purpose: 所有包遵循的设计原则与 fail-closed 约定
audience: 贡献者与评审者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# 设计原则

[English](design-principles.md) | **简体中文**

PubSoft 全部包共享以下规范。新包/新 API 不偏离这些原则，除非文档明确记录为例外。

## Fail-Closed 默认

无标注、未配置、解析失败、校验器缺失时**一律拒绝**而非放行：

- 权限：未声明的权限/未知模块/fail-closed evaluator 返回 401/403，不提供 admin bypass
- 范围：member scope 启用但 `MemberId` 缺失时查询匹配空集，不静默放宽
- 安全：`[RequireClientCredential]` 无注册 verifier 时 403；缺凭证与错误凭证返回相同 401（无枚举 oracle）
- 消息：未知 `MessageType`、重复 alias、payload 类型冲突在注册/查找期拒绝
- 容量：审计 collector / 通知 dedup / 进程内锁容量满时拒绝，不静默丢弃

## 显式 Opt-In，无隐式行为

- 中间件、过滤器、health endpoint、EF mapping、审计采集（IP/UA/actor）、迁移执行——全部要求宿主显式 `Add`/`Use`/`Map`
- 算法由调用方显式选择：RSA 签名与 HS256 签名是独立 API 族，无"自动选一个"，无跨算法降级
- 无程序集扫描、无运行时类型解析（`Type.GetType`）、无约定式注册——discovery 要么是显式注册，要么不存在

## 有界一切

批处理、队列、缓存、collector、限流器、锁表——全部有容量上限并在满时 fail closed。所有异步 API 接受 `CancellationToken` 并传播。

## 反转依赖：宿主提供数据，库提供机制

库不接触凭证存储、权限存储、用户库、配置系统：

- `IOAuthChannelConfigProvider` / `IAlipayAppConfigProvider` — 凭证查询由宿主实现
- `IPermissionGrantProvider` / `IClientCredentialVerifier` / `IEncryptionKeyRing` / `IAuditSnapshotSink` — 同理
- `ITenantContext`/`IMemberScopeContext` — 库消费契约，`Ambient*` 实现是宿主可选装配

## Ambient Scope 规则（AsyncLocal）

`AmbientTenantContext`/`AmbientMemberScopeContext` 的 `Change()` 只影响**当前 async 控制流的下游**。在 callee 内 `Change` 不会回流给调用方——所以 scope 注入必须在包裹 `await next()` 的层（middleware / `IAsyncActionFilter` / `IEndpointFilter`）进行，不能在无 `next` 的 filter 里改。

## 并发与一致性原语分两层

- **lease**（时间界）：Redis/进程内锁，过期自动失效——非正确性保证
- **fencing**（单调 token）：受保护资源须校验，是正确性边界
- 乐观并发：tenant/member scope 列即 EF concurrency token
- 不提供自动续期；不提供"进程内锁当分布式锁用"的伪装

## 持久化语义如实陈述

- Outbox 投递 at-least-once；Inbox receipt 只保证同事务数据库写入，不管外部副作用
- 审计归档 export-before-delete；同 DbContext 导出时归档+删除原子，否则 at-least-once
- 保留期/归档/清理全是显式宿主调用原语，无后台自动调度

## AOT 策略

- **叶子包**（不依赖 EF Core / ASP.NET MVC / ABP）：标注 `IsAotCompatible`，IL 警告即错误，由 `tests/PubSoft.AotSmoke` 真实 `PublishAot` 验证
- 宿主-提供-类型的 STJ 点：`JsonSerializerOptions` 契约（宿主注入 source-gen context），代码内标 `UnconditionalSuppressMessage` 说明
- 库内固定类型：source-gen `JsonSerializerContext`
- 运行时类型解析路径：标 `RequiresUnreferencedCode`/`RequiresDynamicCode` 到公共 API 边界，诚实标记而非隐藏
- EF/ASP.NET/ABP 依赖包不做 AOT 妥协——上游不支持

## 安全原语的边界

- 密钥/凭证不进日志、不进异常消息
- PBKDF2 hasher 支持 `VerifyAndRehash` 透明升级；AES 只写 GCM、读旧 CBC 仅迁移用途
- 不内置任何默认密钥/默认凭证/开发后门
- 时序敏感比较用 `CryptographicOperations.FixedTimeEquals`

## 包分层

- `PubSoft.{Area}`：零框架依赖的核心契约与实现
- `PubSoft.{Area}.{Tech}`：技术适配（`.AspNetCore`、`.Redis`、`.EntityFrameworkCore`、`.Http`、`.Outbox`）
- 包间依赖由 `scripts/verify-package-dependencies.py` allowlist 把关
