# 变更日志

PubSoft 各包的重要变更。格式遵循 [Keep a Changelog](https://keepachangelog.com/)；
0.x alpha 阶段按 semver 管理——破变更会明确标注。

## [0.1.0-alpha.14] — 2026-11-14

### 破坏性变更
- **持久化 ID 统一为 `long` Snowflake，弃用 `Guid`。** `AuditLogRecord.Id`、
  `AuditEntityChangeRecord.Id`/`AuditLogId`、`OutboxMessage.Id`、
  `InboxReceipt.MessageId`、`IOutboxPublisher.PublishAsync`（现 `Task<long>`）
  及全部 `IOutboxStore`/`IInboxMessageProcessor` 签名由 `Guid` 改为 `long`。
  `LeaseToken` 保留 `Guid`——fencing nonce 非标识符。
- **EF 审计持久化与 Outbox 强制注册 `IIdGenerator`**（`services.AddPubSoftIdGenerator(workerId)`）。
  DI 构建期解析，缺失即 fail-closed——持久化 id 路径不再有 `Guid.NewGuid()` 静默兜底。
- `EfCoreOutboxPublisher<T>` / `EfCoreAuditSnapshotSink<T>` 构造器新增 `IIdGenerator` 必填依赖。
- `SchedulerEngine` 构造器新增 `IDistributedLock?` 参数（多副本互斥）。

### 新增
- `IIdGenerator`（`PubSoft.Core`）——客户端 `long` id 策略缝；
  `SnowflakeIdGenerator` 为内置 53 位 JS 安全实现。
- `AddPubSoftIdGenerator`（`PubSoft.DependencyInjection`）——三种注册：
  显式 `workerId`、options 委托、`PubSoft:IdGenerator:WorkerId` 配置绑定。
  workerId 缺失/越界注册期抛错；`TryAdd` 让位宿主自有生成器。
- `LegacyIdMap` + `ConfigurePubSoftLegacyIdMaps()`——Guid→long 迁移 shim：
  `(EntityType, LegacyId)` → `NewId` 唯一映射，迁移完成可退役。
- `ConfigEncryptionKeyRing` + `EncryptionKeyRingOptions`——内置 config 驱动
  `IEncryptionKeyRing`（`ActiveKeyId` + `Keys:{id}=base64`）；KMS 实现仍走契约。
- `IClientSignatureVerifier` + `ClientSignatureRequest`（`PubSoft.AspNetCore`）——
  签名客户端凭证缝：`X-Signature` 头存在时宿主 verifier 完全拥有签名协议
  （请求材料 + body 流而非预哈希）；无签名头仍走明文 `IClientCredentialVerifier`。
- `AddCurrentSubjectAuditActor`——`ICurrentSubjectAccessor` 桥接为
  `AuditActorContext`，已解析 subject 零映射入审计。
- `SchedulerOptions.DistributedLocking` + `DistributedLockLease`——每个到期 job
  经宿主 `IDistributedLock` 抢 `pubsoft:sched:{jobName}` 锁；开启但无锁注册全部跳过。
- `RedisHealthCheck` + `AddPubSoftRedisCheck`——PING + 延迟探针，EF 健康检查的
  Redis 对应件。
- `IdentityUserLogin` + `ConfigurePubSoftIdentityUserLogins()`——外部登录绑定
  （`LoginProvider`/`ProviderKey` → user），`(TenantId, LoginProvider, ProviderKey)`
  唯一索引 + `ExtJson` 扩展。
- `HmacSignatureEncoding`/`HmacTimestampUnit`——`HmacValidationOptions` 增加
  编码（LowerHex/Base64）与时间单位（Seconds/Milliseconds）两档 profile，
  兼容 `ClientSigner` 线格式与 hex/秒 对端。
- 每 cid 独立 JWT 签名密钥、可配 JWT claim 方言、身份锁定 + `ExtJson`、
  EF 加密字符串转换、`IEncryptionKeyRing` 缝、审计 `Detail` 映射。

### 变更
- 全部持久化 `long` 主键强制 `ValueGenerated.Never`
  （`ApplySnowflakeIdConventions`）——EF 不会回填 identity/NEWID。
- `IdentityUser` 增加锁定字段与 `ExtJson` 扩展列。
- 机器 token 在 `IClientSigningKeyProvider` 解析到密钥时按 cid 独立验签。

### 文档与工具
- `PUBSOFT037`——源码 `///` 注释必须 ASCII/英文；zh-CN 经
  `lib/{tfm}/zh-CN/` 卫星 IntelliSense 管线下发（VS 按 UI 语言自动取）。
- `PUBSOFT038`——公开/受保护成员必须有 `///` 文档；按包经
  `PubSoftDocDiscipline=full` 门控。42 包全部扫至零违规（约 1.5k 成员）。
- `docs/host-integration-checklist` 新增「标识生成」节；11 包 README 双语
  同步；docs 索引补治理与 checklist 条目。

## [0.1.0-alpha.11] — 2026-11-05

### 变更
- `EfCoreHealthCheck<TDbContext>`：基类/抽象上下文类型改在注册期拒绝
  （`ArgumentException`），不再永远 Unhealthy——须传 `AddDbContext` 注册的
  具体类型。支持 `IDbContextFactory<T>` 注册的上下文。
- `IPubSoftModule`：新增 `OnInitializationAsync`/`OnShutdownAsync`（sync 钩子
  桥接兼容）；`ModuleOrchestrator` 改为异步优先。
- `AddPubSoftMachineTokens`：`ITokenProvider` 同时注册 keyed `"machine"`
  服务——多提供方共存时经 `[FromKeyedServices(MachineTokenProvider.ServiceKey)]`
  确定性解析。

### 新增
- `WebModuleContext` + `UsePubSoftModulesAsync`——web 宿主模块生命周期胶水；
  `StartPubSoftModulesAsync` 覆盖通用宿主。
- `MachineTokenOptions.IsValid`——启动期 issuer/audience/key-id 卫生、
  生命周期上限、256-1024bit 签名密钥校验。

### 文档 / 工具
- 三层文档治理 + 模板 + 变更触发矩阵。
- `LLMS.txt` AI 集成索引；提交门禁脚本（`check-commit.sh`、
  `check-doc-drift.sh`、`verify-before-push.sh`）。
