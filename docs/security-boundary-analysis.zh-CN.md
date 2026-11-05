# PubSoft 安全与信任边界分析

[English](security-boundary-analysis.md) | **简体中文**

评审基线：`main` / `origin/main`，提交 `81a7f52`（2026-11-04）。本文总结仓库证据支持的安全机制、信任边界、宿主责任与证据缺口。它不是对任何生产部署的渗透测试或安全认证。

## 范围与解读

PubSoft 是一组显式启用的 .NET 基础设施库与适配器，而不是完整的应用身份、授权、密钥管理或网络策略。结论基于当前实现、包文档、测试与工作流。第三方行为、生产配置与消费方应用代码通常无法从本仓库确立。

本文使用的证据标签：

- **库内强制（Library-enforced）：** 由当前包直接实现的校验、拒绝或资源上限。
- **宿主配置（Host-configured）：** 消费方必须注册、排序或提供的机制；库无法保证集成正确。
- **部署相关（Deployment-dependent）：** 需要网关、密钥库、Redis、数据库、身份提供方或运维配置。
- **未确立（Not established）：** 仓库证据不足以得出确定性结论。

风险等级描述在既定前置条件下的影响与可利用性：高 / 中 / 低。条件性风险必须连同其部署假设一起阅读。通过的测试只表明覆盖用例的预期行为，并不证明不存在其他缺陷。

## 系统与信任边界

典型请求与数据流：

```text
外部客户端 / 服务 / 设备
  -> 反向代理或 API 网关（源 IP、转发头、TLS 信任）
  -> PubSoft ASP.NET Core 适配器（认证、租户上下文、授权、限流、审计）
  -> 宿主业务逻辑与权限/凭据解析器
  -> EF Core 数据库 / Redis / 消息代理 / 外部 HTTP 服务
```

Scheduled HTTP 与 Outbound 也可以从宿主后台任务直接调用外部系统，绕过 HTTP 入口。因此请求入口控制不能替代任务级授权、出方向网络策略或外部副作用的幂等性。

### 资产与攻击者能力

主要资产包括租户与成员数据、身份与权限、口令与服务凭据、签名/加密密钥、nonce 与会话、审计记录、消息与外部副作用，以及应用与构建产物的完整性。相关攻击者可能是匿名互联网请求方、权限受限的已认证用户、恶意或被攻陷的租户/服务客户端、能编辑低可信任务配置的操作员，或能向公开端点提交任意头与载荷的调用方。

请求数据、客户端头、查询参数、消息载荷，以及低可信用户可编辑的任务配置默认不可信。可信代理、宿主身份提供方、密钥库、分布式存储与生产网络拓扑必须由消费方声明并验证。若宿主允许用户选择租户、client code、Outbound profile 或任务目标，该选择需要单独的授权；库 API 本身不构成授权证明。

## 控制与责任矩阵

| 边界 | 仓库内机制 | 主要责任与限制 |
|---|---|---|
| JWT、OAuth、subject | RS256/HS256 分离校验路径；subject 解析器可要求可信 issuer；OAuth provider 使用显式配置与适配器 | 宿主配置 issuer/audience、授权码/回调、账号绑定、会话生命周期，以及 subject 到本地账号的映射。第三方兼容性需单独评审。 |
| 权限、客户端凭据 | 授权按 subject、tenant、client code、module 精确过滤；未定义权限与缺失 verifier 均 fail closed | 宿主必须覆盖每个受保护端点，提供可信 grant/verifier 实现，并定义管理员豁免与租户切换规则。MVC/endpoint filter 需显式 opt-in。 |
| 租户/成员与 EF | `HttpContextTenantContext` 默认不读租户头；EF 命名过滤器覆盖 tenant/member/soft-delete；member 作用域缺少成员 ID 时返回零行 | 仅在可信边缘剥离外部值且主体有权选择该租户后才启用 header 选择。`IgnoreScopeFilters()` 与 EF `IgnoreQueryFilters()` 是特权旁路，调用点需要管控。 |
| 口令、加密、签名 | PBKDF2 口令散列；AES-GCM 写入；CBC 仅迁移读取；HMAC 定时常量比较；nonce 在签名验证通过后才占用 | 宿主提供 `IEncryptionKeyRing`、应用 secret 解析器、轮换/备份/恢复与访问策略。内存 nonce 仅覆盖单进程；分布式存储需要原子操作与故障策略。 |
| 入口、代理、限流 | 转发头适配器支持可信代理；固定窗口限流器限制 identifier 容量；限流策略可叠加 | 代理必须剥离伪造的转发头，且 `UseForwardedHeaders` 必须先于 IP 消费方。限流是进程内的，不是集群级配额或并发边界。 |
| 定时 HTTP 与出站 | Scheduled HTTP 默认阻断私网地址，连接时解析并固定目标，逐跳重校验重定向，不跨源转发凭据；重试默认仅限安全方法 | 宿主控制任务编写、allowlist、内网探测豁免、外部 HTTP 凭据/传输/日志与幂等性。Outbound 元数据 `IsEncrypted` 不执行加密。 |
| Edge RPC 与会话 | handler 显式声明 scheme；会话存储与限流可插拔；scheme 含 HMAC、session、none 与遗留 MD5 | `none` 路由必须是有意公开；MD5 仅兼容用途，不用于新部署。内存会话不适合多实例；宿主控制伪路径与 OpenAPI/Swagger UI 暴露。 |
| EF、审计、消息 | EF 写入检查作用域；审计值捕获 opt-in 且支持敏感字段掩码；Outbox/Inbox 提供事务性数据库处理并声明 at-least-once 投递 | 宿主拥有 DB 账号/迁移、跨租户运维授权、审计访问/留存、broker 权限与消费方副作用幂等。清理/归档不自动调度。 |
| 构建与供应链 | CI 含 Gitleaks、高/严重 NuGet 公告硬门禁、测试/覆盖率阈值、依赖更新、包与 AOT 消费者检查 | 当前工作流不等同于完整 SAST/DAST、签名发布或生产产物溯源。覆盖率与消费者构建不证明安全属性或部署安全。 |

## 安全相关公共 API 清单

以下清单把各项目 API 台账中的安全相关契约映射到消费方必须做的决策。它不是逐符号清单：每行聚合相关契约并突出会实质改变信任边界的选项。下方的逐包对账覆盖了台账所代表的全部源码项目，并指出其安全相关面在何处讨论；没有直接安全边界的工具性 API 不逐一分析。公共 API 的存在本身不是漏洞。下列默认值与行为已对照实现或包 README 核实。

| 包/API 面 | 安全相关契约 | 消费方所需决策与证据边界 |
|---|---|---|
| `PubSoft.AspNetCore`：`AddPubSoftPermissionAuthorization`、`AddPubSoftClientCredential`、`PermissionEndpointFilter`、`ClientCredentialEndpointFilter`、`IClientCredentialVerifier` | 授权 filter 显式 opt-in；客户端凭据经可配置头传入并委派给宿主 verifier；verifier 结果可设置 client ID、tenant ID 与 claims。 | 枚举受保护路由并证明每条被覆盖。校验 secret 时不得记日志；把返回的 tenant/claims 绑定到已验证凭据与最小权限范围。公共 API 声明边界，不证明宿主 verifier 正确。 |
| `PubSoft.Authentication.Jwt.AspNetCore`：`PubSoftJwtBearerOptions`、`DualJwtOptions`、`JwksOptions`、`JwksKeyProvider` | 公共设置含 issuer、audience、metadata/JWKS 来源与对称密钥备选；JWKS 缓存期可配。Dual JWT AuthServer 曾有单独报告的 audience 校验与 key 缓存生命周期发现 R2-R3，均已在基线后修复。 | 固定接受的 issuer、audience、算法与密钥来源；metadata 端点置于管理控制下；演练密钥轮换与跨 audience 拒绝。不要把普通 bearer 路径的校验推及 Dual JWT。 |
| `PubSoft.AspNetCore`：`HttpContextTenantOptions.ReadHeader`、`ClaimNames`、`HeaderName`；`PermissionEndpointFilter` | 租户头读取默认关闭；启用的头是租户选择器而非身份证明。授权以 subject 与租户上下文为输入。 | 只允许经服务端验证的主体选择其有权访问的租户。在真实仓储路径上测试跨租户对象访问、缺失租户/成员上下文与特权过滤器旁路。 |
| `PubSoft.AspNetCore`：`PubSoftForwardedHeadersOptions` 与 gateway/custom 限流选项 | 适配器中 forwarded-for/proto 处理默认开启；`TrustAnyProxy` 默认 false，`ForwardLimit` 默认 null，可配置精确的可信代理/网段列表。自定义策略接受 `IdentifierSelector` 并限制 identifier 数量；Gateway 有独立的 anonymous/client/failure 桶。 | 只配置真实代理对端与代理链上限；隔离后端并在 IP 消费方之前运行该中间件。除非在认证之后派生，自定义 identifier 一律视为不可信；保留聚合限流。Gateway 的独立信任假设见 R1 与 R6。 |
| `PubSoft.Security`：`HmacSignatureValidator`、`INonceStore`、`MemoryNonceStore`、`RateLimiter`；ASP.NET Core `HmacValidationOptions` | 签名输入含 method、path、query 与可选 body digest；豁免路径前缀与最大 body 可配。body 哈希默认开启（`RequireBodyHash`，默认 1 MiB，硬上限 16 MiB）。内存 nonce 存储默认上限十万条且进程内有效。 | 收窄旁路前缀、按请求语义要求 body digest、限制请求体、保护 secret 解析，并在多副本间使用原子共享 nonce 存储。确保每个入签字段与代理规范化及客户端 canonicalization 一致。 |
| `PubSoft.Scheduling.Http`：`HttpExecutorOptions`、`HttpExecutorConfig`、`TokenRef` | 私网阻断默认开启；宿主 allowlist 默认空；重定向与系统代理默认关闭；不安全重试需 opt-in。options 校验拒绝 `UseSystemProxy` 与 `BlockPrivateNetworks` 组合。凭据与任意头可经任务配置提供。 | 限制可创建/编辑任务的主体；可行时设置显式宿主 allowlist；无书面豁免不启用私网目标、代理或不安全重试。安全的执行器无法授权任务作者，也无法保证连接路径之外的网络安全。 |
| `PubSoft.Contracts` / `PubSoft.Outbound`：`ApiFieldAttribute.IsEncrypted`、`IOutboundSdk.ExecuteAsync(request, profileId, tenantId)` | `IsEncrypted` 是元数据而非密码学变换。Outbound 调用接受 tenant 与 profile 选择器，但其本身不做授权。 | 调用前强制 tenant/profile 授权，并在具体 provider 中保护凭据、传输、日志与重试。不要把元数据或必填参数当作强制手段。 |
| `PubSoft.Edge.Client` / `PubSoft.Edge.Core` / `PubSoft.Edge`：`EdgeClient` 构造函数、`EdgeAuthScheme.None/Md5`、`EnablePseudoPaths`、`TimestampToleranceSeconds`、OpenAPI 选项 | 一个客户端构造函数默认 `None`；handler scheme 默认按包 README 为 HMAC + session。MD5 与 `None` 仍作为公开兼容/匿名 scheme。伪路径默认关闭；OpenAPI 默认仅 Development，Swagger UI 使用可配置 CDN 资源。 | 客户端显式选择 scheme；`None` 只用于有意公开的 handler，MD5 只用于遗留互通。生产环境管控方法目录与文档，评审 CDN 策略，不向最终用户返回 `subMessage` 诊断。 |
| `PubSoft.Messaging.Mqtt`：`MqttOutboxOptions`、`MqttInboxOptions` | 公共选项含 broker URI、client identity、username/password、QoS、持久会话、topic/filter 与载荷上限。README 要求 MQTT 5 提供 Inbox 去重元数据；MQTT 3.1.1 会静默丢失去重标识。 | 使用 `mqtts://` 与 broker ACL，凭据取自 secret provider，按需按服务/租户约束 topic 权限，并测试重连、重复投递、离线队列与 broker 版本行为。Outbox 仍是 at-least-once；幂等依然必需。 |
| `PubSoft.Cryptography`：`IEncryptionKeyRing`、`AesGcmEncryptor`、`CredentialEncryptionService`、`Sm4.EncryptEcb/DecryptEcb`；审计捕获选项 | AES-GCM 使用宿主 key ring；底层 SM4 ECB API 公开且标注仅兼容用途，因 ECB 泄露分组模式。审计的 value/actor/IP/UA 捕获可配且可能含敏感数据。 | 密钥保管/轮换不入源码库；新存储优先认证加密；ECB 限于强制遗留协议。最小化捕获数据、掩码 secret，并执行审计访问与留存控制。 |
| `PubSoft.Tokens`：`MachineTokenOptions`、`MachineTokenCredential`、`TokenCredential`、`TokenRequestOptions`、`HttpTokenProvider`、`TokenDelegatingHandler` | 支持本地 HS256 机器令牌与可配置 URL/method/body/headers 及 JSON token/expiry 路径的 HTTP 取令牌。缺失所选凭据/令牌 fail closed；无凭据时请求原样通过。401 重放显式 opt-in 且最多重试一次，但会缓冲请求体；token 响应无包级大小上限。 | 签名密钥与端点凭据取自 secret manager，使用 HTTPS 与可信端点 URL，隔离缓存凭据键，并在宿主/传输边界限制 token 响应与重放体大小。仅对语义幂等且体有界的操作 opt-in 401 重放。 |
| `PubSoft.Authentication.OAuthProviders`：`OAuthAuthorizeUrlRequest`、`IOAuthProvider`、`IOAuthProviderRegistry`、`IOAuthChannelConfigProvider` | 公开流程输入含 `State`、`CodeChallenge`、`RedirectUri`、provider/channel/app 选择器与 scope。provider 返回厂商身份数据；宿主配置提供凭据与 redirect URI。 | 生成一次性 state/nonce 并绑定到 user-agent 事务，支持处使用 PKCE，精确 allowlist redirect URI，并授权 provider/channel/app 选择。回调校验、账号绑定、会话/令牌签发与身份到账号映射归宿主。 |
| `PubSoft.MultiTenancy`：`ITenantAppService`、`CreateTenantInput`、`UpdateTenantInput`、`Tenant` | 管理面只是框架无关的契约/模型；无持久化实现、租户解析器或管理员授权。tenant code 经模型 API 保持稳定；生命周期/删除语义由宿主定义。 | 将 create/list/update/delete 按管理面保护，校验租户生命周期与标识唯一性，绝不把调用方提供的 tenant ID 当作切换作用域的授权。 |
| `PubSoft.Auditing` / `PubSoft.AspNetCore` / `PubSoft.EntityFrameworkCore`：`AuditChangeCollectorOptions`、`AuditScopeOptions`、`AuditMiddlewareOptions`、`AddPubSoftAuditChangeCapture`、留存清理 | 属性值、actor、client IP 与 UA 捕获可配；有值上限与敏感属性掩码。审计与留存 API 不确立谁可读/导出/删除记录或清理何时运行。 | 最小化并掩码采集数据，校验脱敏配置，限制审计访问/导出，设定留存/删除策略，并以最小权限操作身份调度清理。client IP 与 actor 值视为需要可信中间件/身份来源的声明。 |
| `PubSoft.Messaging.Outbox`：`IOutboxScopeProvider`、`OutboxBackgroundService`、`OutboxProcessor` | worker opt-in。无 scope provider 时只处理一个普通 DI scope；多租户 provider 可枚举 scope。投递 at-least-once，数据库 Inbox 事务无法包含外部副作用。 | 宿主对租户枚举做授权，解析处理器前初始化租户上下文，释放产生的 scope，监控重试/死信，并使外部副作用幂等。 |
| `PubSoft.Notifications`：`DingTalkWebhookOptions`、`TeamsWebhookOptions`、dispatcher/去重选项 | webhook URL 与 DingTalk secret 是可配凭据；内置通道使用有界 HTTP 弹性。分发去重有界且进程内；多通道分发顺序执行、非原子。 | URL/secret 存放于批准的 secret 存储，目的地变更限于可信操作员，执行出方向策略，脱敏接收方/provider 数据，持久或跨实例投递保证用 provider 侧幂等。 |
| `PubSoft.Resilience.Http`：`HttpResilienceOptions`、`PubSoftHttpResilienceHandler` | 配置总/单次超时、重试与可选进程内熔断。重试仅限无 body 的 GET/HEAD/OPTIONS/TRACE；无不安全重试开关。 | 预算匹配上游容量与业务期限。不要仅凭 HTTP 方法推断应用级幂等；熔断状态不分布式。 |
| `PubSoft.Caching.Redis`：`IFencedLockHandle`、分布式锁/缓存选项 | Redis 锁获取使用 owner token 与单调 fencing token；租约续期显式而非自动。租约本身不能保护下游资源免受过期持有者影响。 | 把 fencing token 传给受保护资源并要求其拒绝过期 token；审慎选择租约/续期行为并测试 Redis 故障转移/拓扑。缓存键前缀与租户隔离仍是宿主设计决策。 |

### 逐包台账对账

本对账在包级别完整，而非逐符号级别。一条声明按台账中非空、非注释行计。本检出中所有 `PublicAPI.Shipped.txt` 台账均为零声明；全部 API 契约以未发布形式记录在 `PublicAPI.Unshipped.txt` 台账中。因此检入的台账快照把所列 API 契约记为未发布；这不构成对包发布状态的断言。包列链接到其 unshipped 台账。"已分析"表示其安全相关契约组在上文或所引章节讨论；"仅登记"表示该包已入账但并未声称做了直接的安全契约评审。

| 包 / 台账 | 本报告中的对账 |
|---|---|
| [PubSoft.Analyzers](../src/analyzers/PubSoft.Analyzers/PublicAPI.Unshipped.txt) | 仅登记：构建期诊断；未评估运行时信任边界。 |
| [PubSoft.Contracts](../src/core/PubSoft.Contracts/PublicAPI.Unshipped.txt) | 已分析：DTO/属性元数据与描述性 `IsEncrypted` 边界。 |
| [PubSoft.Core](../src/core/PubSoft.Core/PublicAPI.Unshipped.txt) | 仅登记：通用基元；未声称包级安全契约。 |
| [PubSoft.Cryptography](../src/core/PubSoft.Cryptography/PublicAPI.Unshipped.txt) | 已分析：认证加密、key-ring 边界、兼容 ECB 与审计捕获。 |
| [PubSoft.DependencyInjection](../src/core/PubSoft.DependencyInjection/PublicAPI.Unshipped.txt) | 仅登记：注册组合；有效服务图归宿主。 |
| [PubSoft.Domain](../src/core/PubSoft.Domain/PublicAPI.Unshipped.txt) | 已分析：ambient 租户/成员作用域在租户与 EF 边界下覆盖。 |
| [PubSoft.Events](../src/core/PubSoft.Events/PublicAPI.Unshipped.txt) | 仅登记：事件契约/分发；事件授权与 handler 信任随应用而定。 |
| [PubSoft.Identity](../src/core/PubSoft.Identity/PublicAPI.Unshipped.txt) | 按域分析：账号/凭据生命周期与宿主身份映射；见身份边界细节。 |
| [PubSoft.Logging](../src/core/PubSoft.Logging/PublicAPI.Unshipped.txt) | 仅登记：未做专门的日志 API 安全评审；secret 与个人数据脱敏仍是宿主责任。 |
| [PubSoft.Mapping](../src/core/PubSoft.Mapping/PublicAPI.Unshipped.txt) | 仅登记：映射配置可能影响属性暴露；DTO 授权随宿主而定。 |
| [PubSoft.MultiTenancy](../src/core/PubSoft.MultiTenancy/PublicAPI.Unshipped.txt) | 已分析：管理契约、稳定租户标识与缺少内置管理员授权。 |
| [PubSoft.Resilience](../src/core/PubSoft.Resilience/PublicAPI.Unshipped.txt) | 按域分析：重试/幂等与进程内熔断考量；HTTP 集成细节见下。 |
| [PubSoft.Auditing](../src/data/PubSoft.Auditing/PublicAPI.Unshipped.txt) | 已分析：值捕获、敏感属性掩码与有界收集。 |
| [PubSoft.Auditing.Outbox](../src/data/PubSoft.Auditing.Outbox/PublicAPI.Unshipped.txt) | 按域分析：持久审计投递/留存在审计与消息下覆盖；宿主调度并授权操作。 |
| [PubSoft.EntityFrameworkCore](../src/data/PubSoft.EntityFrameworkCore/PublicAPI.Unshipped.txt) | 已分析：tenant/member/soft-delete 过滤器、显式旁路、审计捕获与留存。 |
| [PubSoft.AspNetCore](../src/host/PubSoft.AspNetCore/PublicAPI.Unshipped.txt) | 已分析：认证过滤器、租户上下文、转发头、HMAC、限流与审计中间件。 |
| [PubSoft.AspNetCore.Docs](../src/host/PubSoft.AspNetCore.Docs/PublicAPI.Unshipped.txt) | 按域分析：文档/OpenAPI 暴露是宿主部署问题。 |
| [PubSoft.Edge](../src/host/PubSoft.Edge/PublicAPI.Unshipped.txt) | 已分析：handler scheme、伪路径、诊断与 OpenAPI 暴露。 |
| [PubSoft.Edge.Client](../src/host/PubSoft.Edge.Client/PublicAPI.Unshipped.txt) | 已分析：客户端认证 scheme 默认值与显式 scheme 选择。 |
| [PubSoft.Edge.Core](../src/host/PubSoft.Edge.Core/PublicAPI.Unshipped.txt) | 已分析：handler 契约、会话/认证 scheme 与兼容路径。 |
| [PubSoft.Messaging](../src/messaging/PubSoft.Messaging/PublicAPI.Unshipped.txt) | 仅登记：通用消息契约；授权与数据分级取决于消费方。 |
| [PubSoft.Messaging.Mqtt](../src/messaging/PubSoft.Messaging.Mqtt/PublicAPI.Unshipped.txt) | 已分析：broker 凭据、topic/QoS/会话/载荷控制与投递语义。 |
| [PubSoft.Messaging.Outbox](../src/messaging/PubSoft.Messaging.Outbox/PublicAPI.Unshipped.txt) | 已分析：租户 scope provider、worker 注册、重试与 at-least-once 副作用。 |
| [PubSoft.Caching](../src/platform/PubSoft.Caching/PublicAPI.Unshipped.txt) | 仅登记：缓存隔离与键命名空间是宿主责任。 |
| [PubSoft.Caching.Redis](../src/platform/PubSoft.Caching.Redis/PublicAPI.Unshipped.txt) | 已分析：缓存命名空间与分布式锁 fencing/租约语义。 |
| [PubSoft.Modularity](../src/platform/PubSoft.Modularity/PublicAPI.Unshipped.txt) | 仅登记：模块组合/配置；未评估独立安全保证。 |
| [PubSoft.Monitoring](../src/platform/PubSoft.Monitoring/PublicAPI.Unshipped.txt) | 仅登记：可观测性暴露与访问策略随宿主而定。 |
| [PubSoft.Monitoring.Scheduling](../src/platform/PubSoft.Monitoring.Scheduling/PublicAPI.Unshipped.txt) | 仅登记：定时监控注册；运维端点授权随宿主而定。 |
| [PubSoft.Notifications](../src/platform/PubSoft.Notifications/PublicAPI.Unshipped.txt) | 已分析：webhook URL/secret、目的地信任、有界去重与分发语义。 |
| [PubSoft.Outbound](../src/platform/PubSoft.Outbound/PublicAPI.Unshipped.txt) | 已分析：tenant/profile 选择器、凭据、传输与重试归属。 |
| [PubSoft.Resilience.Http](../src/platform/PubSoft.Resilience.Http/PublicAPI.Unshipped.txt) | 已分析：超时/重试安全约束与进程内熔断。 |
| [PubSoft.Scheduling](../src/platform/PubSoft.Scheduling/PublicAPI.Unshipped.txt) | 按域分析：任务编写、权限与运行边界归宿主。 |
| [PubSoft.Scheduling.EntityFrameworkCore](../src/platform/PubSoft.Scheduling.EntityFrameworkCore/PublicAPI.Unshipped.txt) | 按域分析：持久化任务配置与授权是宿主责任。 |
| [PubSoft.Scheduling.Http](../src/platform/PubSoft.Scheduling.Http/PublicAPI.Unshipped.txt) | 已分析：SSRF 控制、重定向、代理/私网开关与任务供给的凭据。 |
| [PubSoft.Tokens](../src/platform/PubSoft.Tokens/PublicAPI.Unshipped.txt) | 已分析：机器令牌签名密钥、HTTP 令牌端点、缓存标识与 opt-in 重放；token 响应大小包内不设上限。 |
| [PubSoft.Authentication](../src/security/PubSoft.Authentication/PublicAPI.Unshipped.txt) | 按域分析：认证抽象与 subject 解析；宿主身份生命周期在其之外。 |
| [PubSoft.Authentication.Jwt](../src/security/PubSoft.Authentication.Jwt/PublicAPI.Unshipped.txt) | 已分析：签名算法与令牌声明；见 JWT 风险与部署清单。 |
| [PubSoft.Authentication.Jwt.AspNetCore](../src/security/PubSoft.Authentication.Jwt.AspNetCore/PublicAPI.Unshipped.txt) | 已分析：bearer/Dual JWT 校验、JWKS 缓存与 DI 生命周期发现。 |
| [PubSoft.Authentication.OAuthProviders](../src/security/PubSoft.Authentication.OAuthProviders/PublicAPI.Unshipped.txt) | 已分析：state/PKCE/redirect 输入、provider 凭据与宿主回调/账号绑定边界。 |
| [PubSoft.Authorization](../src/security/PubSoft.Authorization/PublicAPI.Unshipped.txt) | 已分析：grant 过滤、verifier 契约与显式端点覆盖。 |
| [PubSoft.Security](../src/security/PubSoft.Security/PublicAPI.Unshipped.txt) | 已分析：HMAC、nonce、限流与 secret 解析契约。 |
| [PubSoft.Security.Redis](../src/security/PubSoft.Security.Redis/PublicAPI.Unshipped.txt) | 已分析：分布式 nonce 占用与 Redis 可用性/故障转移边界。 |
| [PubSoft.Testing](../src/testing/PubSoft.Testing/PublicAPI.Unshipped.txt) | 仅登记：测试辅助件不是运行时控制，也不证明消费方部署安全。 |

本对账不声称台账中的声明逐条经过安全评审。它确立包级入账并把安全相关组指向分析；"仅登记"是明确的限制声明，不是健康证明。部署的 HTTP 端点无法从这些库台账枚举，因为端点由消费宿主定义。

本清单明确了责任划分：NuGet 公共 API 定义可用机制与 opt-in 开关；消费宿主决定注册、端点覆盖、身份/secret provider 与中间件顺序；部署层控制代理可达性、broker/DB ACL、出方向、TLS 与运维监控。API 台账是有用的评审输入，但不是安全规格，也不证明所有组合都安全。

## 外部指引对照

此处以 OWASP API Security Top 10（2023）作为覆盖问题的分类法，而非发现计数，也不声称每一类都适用。

| OWASP API 风险 | 需评估的 PubSoft 面 | 评估 / 待证事项 |
|---|---|---|
| API1 对象级授权失效 | 权限过滤器、租户上下文、EF 作用域过滤器、Outbound tenant ID | 机制存在，但路由/资源授权、过滤器旁路与消费方业务查询需要宿主级负向测试。 |
| API2 认证失效 | JWT、Dual JWT/JWKS、HMAC、客户端凭据、Edge 会话 | 提供多种认证机制；按部署验证 issuer/audience/密钥生命周期、secret 校验、重放行为与会话存储。R2-R4 曾是具体的条件性发现，均已在基线后修复。 |
| API3 对象属性级授权失效 | `ApiFieldAttribute.IsEncrypted`、DTO/契约、审计值捕获 | 加密元数据是描述性的，不强制字段级机密性或授权。在每个宿主中评审序列化、批量赋值与敏感字段策略。 |
| API4 资源消耗无限制 | Gateway/自定义限流、口令工作因子、请求体上限、队列与 worker 池 | 部分控制有界且进程内；不确立集群级公平性、并发上限、登录防滥用或全部载荷/队列上限。R1 曾是条件性配额分区风险，已在基线后修复。 |
| API5 功能级授权失效 | 权限 endpoint/MVC filter、客户端凭据 filter、Edge handler scheme | opt-in filter 与逐 handler scheme 必须一致应用。盘点端点并测试未认证、错角色、错客户端与跨租户调用。 |
| API6 敏感业务流访问无限制 | 登录/validate、OAuth 流程、出站任务、Edge 方法、管理 API | 业务流限流与事务授权随应用而定；在消费方验证配额、滥用检测与 step-up/审批控制。 |
| API7 服务端请求伪造 | Scheduled HTTP、JWKS metadata/JWKS URI、OAuth provider 配置 | Scheduled HTTP 文档化了连接时地址固定与逐跳重定向校验；宿主 allowlist 与禁用不安全代理组合仍是部署决策。身份 metadata URL 保持可信且固定。 |
| API8 安全配置错误 | `TrustAnyProxy`、转发头链、`ReadHeader`、`UseSystemProxy`、OpenAPI、`None` scheme | 高影响选项是公开的，其中一些明确标注在受限拓扑外不安全。验证生产生效配置而非仅源码默认值。R6 以代理拓扑为条件，已在基线后部分缓解。 |
| API9 资产清单管理不当 | 逐包公共 API 台账、API 文档、Swagger/OpenAPI、包消费者 | 逐包台账与消费者冒烟项目有助于资产盘点。`scripts/sync-public-api.sh --check` 存在，但所评审 CI 工作流未显式调用；路由清单与外部 API 版本仍是宿主责任。 |
| API10 API 消费不安全 | OAuth provider 适配器、MQTT/Outbox、出站 HTTP、错误传播 | 校验上游 TLS/身份、响应模式与信任边界；清洗上游错误并使外部副作用幂等。MQTT README 记录了 MQTT 5 依赖与 at-least-once 语义。 |

与发现最直接相关的外部建议：RFC 8725 要求应用选定 JWT 算法，并在 issuer/audience 声明用于区分接收方时校验之；Microsoft ASP.NET Core 指引要求转发头只来自可信代理/网段，且先于依赖它们的中间件处理；OWASP 多租户指引把客户端提供的租户标识视为需要服务端授权的选择器；OWASP 口令指引建议自适应散列并平衡工作因子与登录期拒绝服务风险。这些建议为宿主清单提供依据；它们本身不确立 PubSoft 缺陷。

## 证据方法与限制

公共面发现使用各项目 `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` 台账、包 README、实现与邻近测试。台账由 `Microsoft.CodeAnalysis.PublicApiAnalyzers` 维护；[打包指引](packaging.md) 记录了 `scripts/sync-public-api.sh --check` 作为可用的漂移检查，而所评审的 [CI 工作流](../.github/workflows/ci.yml) 未显式调用它。这是值得考虑的流程缺口，并非检入台账当前有误的证据。

生成的[覆盖率矩阵](test-coverage-matrix.md)报告 2,195/2,608 成员（84%），并明确排除接口成员。未覆盖的具体实现包括 EF 权限/仓储/数据作用域 provider、审计 sink/留存、OAuth registry/provider 路径、Edge handler/会话存储与 Outbox/Inbox 组件。把它当作优先级辅助：未覆盖不等于有漏洞，聚合覆盖率也不度量安全断言。安全优先的后续应针对不变量，如跨租户拒绝、过滤器缺失行为、JWT 密钥移除、重定向重校验、跨副本重放与 secret/broker 失败关闭。

外部材料查阅于 2026-11-04。它们提供规范性或社区指引，而非消费方配置的证据。OWASP API Top 10 是风险分类法，OWASP Cheat Sheets 是实现指引，RFC 8725 是 IETF 最佳当前实践，Microsoft Learn 描述 ASP.NET Core 10 行为，NIST SSDF 是开发过程框架。未测试任何生产部署、运行时流量、云账号或消费方包集成。

## 优先级风险与缺口

以下各项已在基线 `81a7f52` 复核。严重级是面向消费方的风险排序，不是漏洞公告或 CVSS 评分。

### R1：未认证的 Client ID 影响 Gateway 限流分区

**严重级：中；置信度：高。** 在 [GatewayRateLimitingExtensions.cs](../src/host/PubSoft.AspNetCore/GatewayRateLimitingExtensions.cs) 中，`/api/auth/validate` 路由读取 `X-Client-Id`（`Basic` 授权头用户名被接受为等价来源）；任何非空值会选择 `validate-client` 策略而非匿名策略。默认限额分别为每分钟 2,000 与 10 次，客户端桶按 `clientId@IP` 键控。同一标识也参与 failure 限流与默认策略的标识计算。两个来源都不证明客户端凭据已通过验证，因此调用方可改变其取值创建独立分区，削弱聚合节流。通用 RateLimiter 将 identifier 上限设为 100,000；达到容量即拒绝新 identifier。该风险是配额碎片化与容量消耗，而非无界建桶。

**前置条件/影响：** 宿主启用该 Gateway 策略、validate 路由可从不可信网络到达、且边缘未移除或未权威改写该头。它削弱匿名滥用防护；限流器本身不构成认证绕过证明。**建议：** 把匿名限流绑定到可信来源；仅在凭据验证后允许按已认证主体/客户端分区，并保留独立的 IP/全局限流。为任意头取值与失败响应增加回归测试。**状态：** 已在评审基线后修复——伪造客户端身份被双重约束：单 IP 的 clientId 基数上限将超额身份退化到按 IP 桶，且无条件的 `validate-ip`/`oauth-ip` 聚合策略为单地址总请求量封顶。

### R2：JWKS Key-ID 缓存绕过 JWKS TTL

**严重级：中；置信度：高。** [JwksKeyProvider.cs](../src/security/PubSoft.Authentication.Jwt.AspNetCore/JwksKeyProvider.cs) 中的 `_keyCache` 是无过期的 `ConcurrentDictionary`。key ID 命中直接返回缓存键；`CacheMinutes` 只作用于 `IMemoryCache` 中的 JWKS 集合。旧 key 从 JWKS 移除后，该进程仍可无限期接受用旧 key 签名的令牌——只要其余校验通过。

**前置条件/影响：** 使用远端 JWKS 的 Dual JWT 路径，且该 key 已在进程内解析过。撤销响应时间不受配置的 `CacheMinutes` 约束。**建议：** 让按 key 缓存共享 JWKS 生命周期，或在刷新后驱逐已撤销 key；增加模拟密钥轮换/撤销测试并定义重叠期。**状态：** 已在评审基线后修复——按 key 字典已移除；已解析 key 与 JWKS 集合共享 `CacheMinutes` 生命周期，空 JWKS 响应也按 TTL 缓存。

### R3：AuthServer JWT Scheme 不校验 Audience

**严重级：中；置信度：高，影响随部署而定。** [DualJwtAuthenticationExtensions.cs](../src/security/PubSoft.Authentication.Jwt.AspNetCore/DualJwtAuthenticationExtensions.cs) 为 `AuthServerJwks` 设置 `ValidateAudience = false`。若同一 issuer 为多个 API 签发不同 audience 的令牌，本 API 可能接受并非发给它的令牌。

**前置条件/影响：** 该 scheme 已启用、issuer 使用 audience 隔离、且无上层强制目标 audience。issuer、签名与有效期仍会校验；本发现不意味着可伪造任意令牌。**建议：** 配置并校验预期 audience，或文档化 AuthServer 令牌共享等价 audience 边界的理由，并增加跨 audience 拒绝测试。**状态：** 已在评审基线后修复——新增 `JwksOptions.Audience`；配置后 scheme 校验 `aud`（且 `ValidAlgorithms` 固定为 RS256）。未配置仍跳过校验，残余暴露成为宿主显式选择。

### R4：JWKS Provider 经由第二个 DI 容器解析

**严重级：低至中；置信度：高。** 同一扩展在认证选项回调中捕获 `Lazy<JwksKeyProvider>`，并通过 `services.BuildServiceProvider()` 创建。这从服务描述符构建 provider，而非从真实宿主容器解析；singleton、缓存与资源释放生命周期可能发生分裂。可能后果包括依赖实例/配置不一致，以及 provider/HttpClient 资源不随应用容器释放。

**前置条件/影响：** 仅适用于配置了 AuthServer JWKS 的 Dual JWT；具体资源影响取决于注册方式。**建议：** 改用由真实宿主容器解析的认证运行时服务，或其他不构建第二个 provider 的集成方式；验证生命周期与释放。**状态：** 已在评审基线后修复——`Lazy`/`BuildServiceProvider()` 路径已替换为 `AddOptions<JwtBearerOptions>(AuthServerScheme).Configure<JwksKeyProvider>(...)`，provider 经宿主容器解析，生命周期遵循常规 DI 规则。

### R5：HMAC Query-String 文档与行为冲突

**严重级：低（文档一致性）；置信度：高。** [ASP.NET Core README](../src/host/PubSoft.AspNetCore/README.md) 称 query string 不入签。但 [HmacValidationMiddleware.cs](../src/host/PubSoft.AspNetCore/HmacValidationMiddleware.cs) 传入 `Request.QueryString.Value`，[HmacSignatureValidator.cs](../src/security/PubSoft.Security/HmacSignatureValidator.cs) 将其计入签名串（原始 `?…` 形式拼接到 path），且 [HmacSignatureValidatorTests.cs](../tests/PubSoft.Host.Tests/HmacSignatureValidatorTests.cs) 中的 `ValidateAsync_BindsQueryStringIntoSignature` 验证不同 query 不匹配。

**影响：** README 可能误导客户端实现与安全评审，造成互通问题或错误假设。当前实现确实绑定 query；本发现不是说 query 未签名。**建议：** 对齐 ASP.NET Core README、Security Model、签名格式描述与消费者示例。任何格式变更都应版本化。**状态：** 已在收录本文档的检出中修复——AspNetCore 与 `PubSoft.Security` 的 README（中/英文）现描述 `path+rawQuery` 入签格式。

### R6：转发头与 IP 限流依赖可信网关

**严重级：中（条件性）；置信度：高。** Gateway 限流清空 Forwarded Headers 的 `KnownIPNetworks`/`KnownProxies` 并移除跳数上限（`ForwardLimit = null`），以支持可信前置网关拓扑；代码注释与 [ASP.NET Core README](../src/host/PubSoft.AspNetCore/README.md) 要求网关剥离外部供给的 `X-Forwarded-For`。若后端直连可达或代理透传客户端供给值，调用方可影响限流标识与依赖 IP 的审计逻辑。`ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders` 在运行时检测一类常见错配（私网 socket peer 搭配公网宣告客户端），两个限流中间件都会发出一次性告警，但检测不等于防护。

**建议：** 将后端可达性限制在可信代理；对外部转发头覆写而非追加；逐跳验证中间件顺序与信任；通过真实代理部署测试验证服务端实际观测到的 `RemoteIpAddress`。**状态：** 已在评审基线后部分缓解——`GatewayRateLimitOptions.TrustedProxies`/`TrustedNetworks`/`ForwardLimit` 支持固定可信对端（非法条目启动期报错）；两个清单皆空时保留信任任意 peer 的行为以支持纯网关拓扑，部署前置条件仍然适用。

## 补充边界细节

### 身份、授权与租户

- [JWT ASP.NET Core 指引](../src/security/PubSoft.Authentication.Jwt.AspNetCore/README.md)描述了普通 bearer 适配器的 issuer、audience、算法与密钥来源校验。AuthServer Dual JWT 是独立实现路径；不要把普通适配器的保证推及它。
- `AuthenticatedSubjectResolver` 把已认证主体映射为 subject；账号状态、角色解释、租户授权与端点覆盖仍是宿主责任。未显式注册的授权过滤器不保护端点。
- [HttpContextTenantContext.cs](../src/host/PubSoft.AspNetCore/HttpContextTenantContext.cs) 先解析 ambient 作用域；当 `ReadHeader` 启用时其次查配置的租户头；最后查 claims。header 读取为显式 opt-in，且即使代理已净化该头，宿主仍须验证主体有权选择该租户。
- [PubSoftDbContext.cs](../src/data/PubSoft.EntityFrameworkCore/PubSoftDbContext.cs) 建立命名的 tenant/member/soft-delete 过滤器。member 过滤器为 `!IsMemberScoped || (MemberId.HasValue && MemberId == current)`，因此 member 作用域下无成员 ID 时返回零行。作用域过滤器可被显式旁路；无参 EF `IgnoreQueryFilters()` 也会绕过 soft-delete 过滤。测试覆盖代表性查询与仓储作用域，但无法阻止宿主业务代码有意使用旁路。

### 密钥、口令、HMAC 与重放

- [安全与密码学模型](security-cryptography.md)记录了 AES-GCM 信封、PBKDF2、仅迁移 CBC 读取与定时常量比较。密钥生成、保管、访问审计、轮换、备份与恢复属于宿主/密钥管理服务。
- [HMAC 中间件](../src/host/PubSoft.AspNetCore/HmacValidationMiddleware.cs)的豁免路径是按路径段边界匹配的显式旁路；body 哈希默认开启（`RequireBodyHash`）且有界（默认 1 MiB，硬上限 16 MiB）。宿主经 `IAppSecretResolver` 提供 secret；解析器不可用 fail closed 返回 503，appId 无法解析时返回与签名不匹配相同的 401，避免 appId 枚举 oracle。
- 默认 `MemoryNonceStore` 仅在单进程内防重放。[RedisNonceStore](../src/security/PubSoft.Security.Redis/README.md) 以 NX/TTL 原子占用 nonce，但各实例必须共享 Redis 集群、数据库与前缀。Redis 故障转移可能丢失已确认写入，因此仍需部署验证。
- Edge 的 MD5 scheme 是遗留兼容面；新客户端应使用 HMAC。时间戳窗口、签名路径、原始 query 编码、nonce 作用域与代理改写必须两侧一致。

### 数据、审计与消息

- EF 作用域过滤器与仓储检查提供的是机制，而非最小权限 DB 账号、迁移授权、租户目录保护或跨租户运维审批。
- 审计 actor 映射归宿主；IP、UA、actor 与属性值捕获只应为明确的隐私目的启用。敏感字段配置、审计访问与留存需要宿主治理。
- [Outbox 指引](../src/messaging/PubSoft.Messaging.Outbox/README.md)明确声明 at-least-once 投递：数据库 Inbox 事务无法包含邮件、HTTP 或其他系统的提交。外部操作需要幂等键或单独协调；多租户 worker scope provider 必须被授权并正确初始化。`InboxRetentionCleanup` 提供有界删除原语，但不自动调度。

### 外部 I/O、错误响应与资源边界

- [Scheduled HTTP](../src/platform/PubSoft.Scheduling.Http/README.md)默认阻断私网地址，连接时解析并固定目标，逐跳校验手工重定向（至多 8 跳）。启用内网目标或系统代理会改变边界，需要评审；options 校验拒绝 `UseSystemProxy` 与 `BlockPrivateNetworks` 组合，防止代理静默绕过连接时检查。宿主对任务创建/编辑与敏感头注入的控制仍然必要。
- [Outbound](../src/platform/PubSoft.Outbound/README.md)要求显式 tenant ID，但不授权 tenant/profile 选择；传输、凭据、重试、日志脱敏与 provider 生命周期归宿主。
- [Edge 指引](../src/host/PubSoft.Edge/README.md)描述了 OpenAPI 方法目录、Swagger UI CDN 资源与 `subMessage` 中上游诊断细节的暴露边界。生产宿主应限制可见性，避免向最终用户返回内部传输细节。
- 许多队列、缓存、审计收集器与限流器有容量上限，满了即拒绝。这些是可用性边界，不是集群级公平性或拒绝服务防护。宿主还需要请求体、并发、超时、连接与全局配额控制。

## 测试与供应链证据

以下检查在本评审基线执行：

| 证据 | 结果 | 不能证明的事项 |
|---|---:|---|
| `tests/PubSoft.Security.Tests`（SDK 10.0.402） | 557 通过，0 失败 | 不覆盖全部 Dual JWT/JWKS 撤销情形、生产身份提供方或 secret-store 配置。 |
| `tests/PubSoft.Platform.Tests` 中 `SchedulingHttpTests`（SDK 10.0.402） | 62 通过，0 失败 | 不证明生产 DNS、代理、云网络策略或目标服务行为。 |
| `tests/PubSoft.Host.Tests`、`tests/PubSoft.Data.Tests` 与 CI | 已配置在 CI 运行；本评审未全量重跑 | 不应描述为经本评审完整验证。 |

[CI 工作流](../.github/workflows/ci.yml)配置了 Gitleaks（镜像按 digest 固定）、Redis Cluster 与 MySQL 8.4、解决方案测试、70% 联合行覆盖率门禁、pack/消费者/AOT 检查与依赖漏洞报告。[Directory.Build.props](../Directory.Build.props) 将高/严重 NuGet 公告 NU1903/NU1904 视为错误。这些门禁不等同于专用 SAST/DAST、SBOM/产物签名、第三方审计，或对 secret、代理与故障转移的生产验证。本评审未确立这些控制在仓库之外是否存在。

## 宿主部署清单

- 定义接受的 issuer、audience、签名算法、密钥轮换/撤销目标与令牌用途；分别测试普通 bearer 与 Dual JWT 路径。
- 校验每个端点的认证与权限过滤器；验证 subject、tenant、client code 与业务资源间的关系。
- 默认不把租户/客户端身份头当作可信。如必须使用，仅在代理净化与身份验证之后使用，并保留 IP/全局限流。
- 将后端可达性限制在可信代理，覆写外部转发头，并按正确顺序处理。从外部网络验证伪造的头无法改变服务端观测到的源 IP。
- 使用批准的 secret/KMS 存储并演练轮换、泄露响应与恢复。多实例 HMAC 使用原子共享 nonce 存储并验证故障转移。
- 评审 `IgnoreScopeFilters()`、`IgnoreQueryFilters()`、后台 worker 与访问租户数据的管理端点；保护跨租户运维路径。
- 对任务目标应用出方向 allowlist/网络策略。默认不让低可信任务作者选择内网地址、凭据头或不安全重试。
- 定义审计字段、敏感值掩码、访问控制、留存与删除。使外部消息副作用幂等。
- 禁用或保护非开发环境的 Edge OpenAPI/Swagger、`none` 方法与详细错误；生产客户端不使用遗留 MD5。
- 把 Redis/MySQL 故障、代理链行为、限流容量、超时/并发与依赖公告纳入 runbook 与监控。

## 盲区与后续

本分析未验证具体消费应用、云账号、真实网关配置、生产密钥权限、数据库账号、第三方 OAuth provider、broker ACL、Redis 持久性、容器镜像溯源或发布操作。源码评审与现有测试也无法确立真实中间件顺序、端点覆盖、操作员权限或运行时告警质量。

六项发现均已在评审基线后修复或缓解（见各发现的状态注记）；按新选项复验宿主部署仍然必要。进一步建议：考虑静态分析、依赖来源映射/供应链溯源，以及针对生产拓扑的集成测试。

## 主要证据

- [设计原则](design-principles.md) · [安全与密码学模型](security-cryptography.md) · [Permission Client Scope](permission-client-scope.md) · [安全策略](../SECURITY.md)
- [JWT 签名](../src/security/PubSoft.Authentication.Jwt/JwtSigning.cs) · [Dual JWT](../src/security/PubSoft.Authentication.Jwt.AspNetCore/DualJwtAuthenticationExtensions.cs) · [JWKS provider](../src/security/PubSoft.Authentication.Jwt.AspNetCore/JwksKeyProvider.cs) · [权限授权](../src/security/PubSoft.Authorization/PermissionAuthorization.cs)
- [HMAC 中间件](../src/host/PubSoft.AspNetCore/HmacValidationMiddleware.cs) · [Gateway 限流](../src/host/PubSoft.AspNetCore/GatewayRateLimitingExtensions.cs) · [租户上下文](../src/host/PubSoft.AspNetCore/HttpContextTenantContext.cs) · [EF 作用域上下文](../src/data/PubSoft.EntityFrameworkCore/PubSoftDbContext.cs)
- [Security 测试](../tests/PubSoft.Security.Tests) · [Host 测试](../tests/PubSoft.Host.Tests) · [Data 测试](../tests/PubSoft.Data.Tests) · [Platform 测试](../tests/PubSoft.Platform.Tests) · [CI](../.github/workflows/ci.yml)

## 外部参考

- [OWASP API Security Top 10 (2023)](https://owasp.org/API-Security/editions/2023/en/0x11-t10/)
- [OWASP REST Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html) · [Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html) · [Multi-Tenant Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.html)
- [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html) · [Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [Microsoft: Configure ASP.NET Core behind proxies and load balancers (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) · [JWT bearer authentication (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0) · [Rate limiting (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [RFC 8725: JSON Web Token Best Current Practices](https://www.rfc-editor.org/rfc/rfc8725) · [RFC 9700: OAuth 2.0 Security Best Current Practice](https://www.rfc-editor.org/rfc/rfc9700)
- [NIST SP 800-218: Secure Software Development Framework (SSDF) v1.1](https://csrc.nist.gov/pubs/sp/800/218/final)
