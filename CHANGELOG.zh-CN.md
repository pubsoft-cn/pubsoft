# 变更日志

PubSoft 各包的重要变更。格式遵循 [Keep a Changelog](https://keepachangelog.com/)；
0.x alpha 阶段按 semver 管理——破变更会明确标注。

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
