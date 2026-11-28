# 变更日志

PubSoft 各包的重要变更。格式遵循 [Keep a Changelog](https://keepachangelog.com/)；
0.x alpha 阶段按 semver 管理——破变更会明确标注。

## [0.1.0-alpha.24] — 2026-11-28

### 破坏性变更
- **outbox 事件契约键必填。** `IOutboxPublisher.PublishAsync` 必须显式传 `eventType`——CLR 全名回退已移除。审计快照事件使用稳定键 `pubsoft.auditing.snapshot.v1`；按 CLR 类型名匹配的消费者须迁移或先排空 outbox。
- **MQTT inbox 确认改为手动。** `MqttInboxSubscriber` 仅在 inbox 登记加 handler 完成后确认；handler 失败现在触发重投而非静默丢弃。
- **Logging sink 失败行为。** 持续抛异常的 sink 按倍增退避隔离而非每批重试；被跳过批次丢弃。经 `GetSinkStates()` 监控。

### 新增
- `IOutboxEventSerializer` 接缝——trimmed/AOT 宿主可注入显式 JSON 策略与源生成 metadata。
- `EfCoreTransactionExecutor`——fail-closed 单 DbContext 事务件，显式 commit 验证探针。
- `OutboxWorkerStatusTracker`——低基数 worker 状态快照（轮询生命周期、连续失败、批次结果），供宿主健康接线。
- Edge client 源生成信封 context + `JsonTypeInfo` 响应接缝；`EdgeAuthScheme` wire 名跨路径字节一致。
- `LogSinkQuarantineOptions`/`LogSinkState` 诊断面。
- 滚动失败率熔断模式（opt-in）、有界重试抖动、总超时预算内的 `Retry-After` 尊重。
- `BoundedJitter` + AMQP/MQTT inbox 重连 `ReconnectJitterRatio`（默认 0.2）。
- analyzer wave 3：PUBSOFT059-062（`GC.Collect`、恒真 rows-affected 判断、被忽略的 `TryParse`、被丢弃的 fire-and-forget 任务）。

### 安全
- Redis refresh-token family 消费修复 Redis Cluster 语义：family key 显式传入原子 consume 脚本；缺失 token 记录不再能被 consume race 重建。

## [0.1.0-alpha.23] — 2026-11-26

### 修复
- 业务反馈的配置绑定与环境告警重发缺口闭合。
- 全面缺陷评审的持久化与正确性缺口闭合。
- RabbitMQ CI 节点以 rabbitmq 用户启动，prelaunch 后存活。

### 变更
- CI：分层门禁、共享覆盖率门、AMQP 集成覆盖；pre-push 门禁对齐 CI 集成测试环境。

## [0.1.0-alpha.22] — 2026-11-25

### 新增
- 通知与告警健壮性波次；告警规则收件人与模板编码透传。
- AMQP inbox subscriber，补齐 AMQP 消费路径。
- 可读的 scheduler 配置绑定与启动 lint。

### 修复
- HTTP 探针健康检查并行执行。
- 配置解析与 HTTP 失败诊断上浮。
- `AlertJsonContext` 字符串枚举告警规则反序列化。

## [0.1.0-alpha.21] — 2026-11-20

### 新增
- W3C 关联传播与 `PubSoft.Tracing` span 导出器。
- 审计行盖印环境 W3C trace id。
- Native AOT consumer smoke 覆盖 `PubSoft.Tracing`。

### 修复
- 签名、日志与 JWKS 热路径分配裁剪。
- 审计强化轮次：生命周期、边界、fencing 单调性、JWKS 严格边界、调用方-owned 密钥生命周期、可索引关键字。

### 安全
- 传递依赖 `Logging.Abstractions` 经直接引用钉死。

## [0.1.0-alpha.20] — 2026-11-18

### 新增
- 分布式锁上的 fenced executor 与 periodic worker。
- 配置驱动的缓存提供器选择。
- AMQP outbox dispatcher，publisher-confirm 投递。
- EF 分页扩展与慢查询拦截器接线。
- Edge 签名密钥池 readiness 状态面。
- 启动校验检查，详细 options 错误与非空配置读取。

### 修复
- machine token 签名密钥校验按 UTF-8 字节长度计量。

## [0.1.0-alpha.19] — 2026-11-14

### 变更
- 依赖维护波次：Microsoft 10.0.15 + StackExchange.Redis 3.4.35。

### 修复
- allow-list README 示例改用文档前缀替代私网段。

## [0.1.0-alpha.18] — 2026-11-14

### 新增
- 显式空策略的 allow-list 门控件。
- token 凭证 configure-request 钩子；远程 client 凭证验证器 caller-auth 配置钩子。
- JWKS client-assertion 密钥提供器。
- 远程 edge 签名密钥提供器，注册表刷新加本地回退。

### 变更
- analyzer wave 2（PUBSOFT056-058）加 IDE0005/CA53xx 质量门。

### 修复
- HTTP 工厂客户端释放、导出类型守卫、序数字符串比较。

## [0.1.0-alpha.17] — 2026-11-11

### 新增
- 可移植 EF 安全存储与隔离 consumer smoke。
- refresh family 轮换与 token 拒绝列表，memory/Redis/EF 三存储。
- client 凭证升级：远程验证器与 RS256 断言。
- JWT 签发方密钥环与托管 JWKS 端点。

### 安全
- 审计波次：fail-closed 作用域删除与审计查询、migrator compare-and-swap、诊断隔离。

### 修复
- migrator 密钥集分页；审计捕获开关启动告警。

## [0.1.0-alpha.16] — 2026-11-10

### 破坏性变更
- `AuditMiddlewareOptions` 捕获开关（`CaptureClientIp`/`CaptureUserAgent`/`CaptureActor`）默认关闭——静默取证数据丢失风险见升级笔记。
- `AesGcmEncryptor.Decrypt` 改抛 `PubSoftDecryptionException`（`CryptographicException` 子类）；精确类型捕获须迁移。

### 新增
- metadata 绑定的加密列迁移器。
- 类型化解密失败、密钥轮换、配置注册与启动探针。
- 启动诊断管线与升级笔记。
- 双版本流（`alpha.*` 发布 / `dev.*` 内部迭代）。

### 变更
- 评审强化分析器 PUBSOFT045-054 + DocsTool 统一台账 CLI；文档治理清扫。

### 修复
- `Use*` 中间件 null-options 启动崩溃（PUBSOFT055）。
- 外部评审轮：密钥校验、OAuth appid、限流竞态。

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
