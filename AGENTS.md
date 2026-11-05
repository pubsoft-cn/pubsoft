# PubSoft Agent Guide

可复用开源基础设施库。所有包遵循 `docs/design-principles.md`：**fail-closed 默认、显式 opt-in、诚实标注保障边界**。

## 红线

- **提交署名干净**：commit message / PR 描述禁止出现任何工具署名或 AI 协作标记——不加 `Generated with …`、`Co-Authored-By:` trailer、`🤖` emoji 等。commit 内容只描述改动本身。
- **AI 不主动提交**：未经用户明确指令，AI 一律不执行 `git commit`/`git push`/`git reset`/`git add`——改动攒在本地工作区。
- **攒批人审**：AI 把改动归拢成语义节点建议清单（建议 type / 涉及文件 / message 草稿）交用户审核；用户点头后由用户执行提交，或明确授权 AI 代执行。
- **提交含金量**：每个 commit 必须能被单独 review 出完整价值——半成品、凑数 diff、试探性内容不入仓；探索产物（调研清单、未拍板方案）留工作区或 `internal/`，方向定了再提交。
- **净零中间态不推**：push 前过 `git log --oneline origin/main..HEAD`，发现"加了又删/同节点反复修补"的串，先 `reset --soft` 合平再推；推出去的历史不留凑数痕迹。
- **本地=CI 门禁**：push 前必须 `bash scripts/verify-before-push.sh` 全绿——本地没跑的 CI 步骤一律视为隐形门槛。

## 布局

```
src/core/       纯算法/契约件（不依赖 ASP.NET/EF/Redis）
src/platform/   平台能力（Redis/Scheduling/Monitoring/Outbound/Tokens/Caching/Notifications）
src/host/       ASP.NET 宿主件（AspNetCore/Edge 入口网关/ABP 适配器）
src/data/       EF Core / Auditing / Outbox 持久化
src/messaging/  消息骨架
src/security/   JWT/OAuth/Authorization/Cryptography
tests/          统一测试 + PackageConsumerSmoke/SecurityConsumerSmoke 消费者冒烟项目
```

新包默认放 `src/platform/`；只放纯算法进 `src/core/`；ASP.NET 件进 `src/host/`；EF 件进 `src/data/`。

## 构建与测试

```bash
# 全量构建（PublicApiAnalyzers 强制检查 — 新公共符号必须进 PublicAPI.Unshipped.txt）
dotnet build PubSoft.sln -c Release

# 同步 API 基线（修 RS0016）
bash scripts/sync-public-api.sh

# 全部测试（含 Redis/MySQL 集成）
dotnet test tests/PubSoft.Core.Tests -c Release

# 集成测试环境变量（不设置则相关用例自动跳过）
export PUBSOFT_REDIS="localhost:7000"           # Redis 集群（pubsoft-test-redis-cluster 容器）
export PUBSOFT_MYSQL="Server=localhost;Port=3306;Database=pubsoft_test;User=root;Password=..."
```

**不要用跳过模式跑测试**——`Skipped` 会掩盖真 bug（如 Redis 集群下 `KEYS`+`DEL unpack` 清理脚本失效）。CI/交付验证一律带齐 `PUBSOFT_REDIS` + `PUBSOFT_MYSQL`。

## 提交纪律

- **AI 不主动提交** — 只产出"语义节点建议清单"（type + 文件集 + message 草稿）供人审；用户明确说"提交/commit/推"或授权后才执行 git 写操作。默认姿态 = 改好放着，等人审。
- **按语义主题提交，不逐件提交** — 一个 commit = 一个完整语义节点（feature/fix/test-wave/tool），不是一行改动。测试波次攒齐按主题一次提（如 `tests: OAuth exchange paths`、`security: audit round 2`），不逐波 `test && commit && push`。
- **提交信息写 why+what 概要**，格式 `type: summary`（`feat:`/`fix:`/`tests:`/`ci:`/`tools:`/`lift:`/`security:`/`docs:`/`build:`）。
- **push 前检查 `git log --oneline`** — 连续同主题碎片提交先 `git reset --soft` 合一再推，不往主干推流水账。
- **重构历史只在未推或明确协调后** — 已推历史用 `force-with-lease` 重写需明确意图（如本次 lift/security/tests 三段重排）。

## 文档治理

- **front-matter 必备**：`docs/*.md` 与包 README 须带 `purpose/audience/owner/last-reviewed` 元数据；`docs/README.md` 顶部挂 文档→owner 表，季度复审一次、过期即更或删（living library）
- **模板强制**：新包 README 从 `docs/templates/package-readme.template.md` 起步；Scope/Options/Residual Host Responsibilities 三节必须存在
- **示例代码可信**：README 代码块必须能与 PublicAPI 对得上——审阅时逐块核实，后续迁 snippets 编译验证项目
- **生成物豁免**：生成型文档（coverage matrix 等脚本产物）只需 en，豁免 zh 配对

## 编码约定

- **中英文档双写**：`README.md` + `README.zh-CN.md`（`docs/` 同规则）
- **AOT**：纯托管叶子包声明 `IsAotCompatible`；反射 JSON 件不标（Edge Client 用 `System.Net.Http.Json` 即反射——如要 AOT 需加源生成 `JsonSerializerContext`）
- **无业务泄漏**：公开源码/README/注释不得出现内部组织标识/内部域名/具体业务示例——示例一律用 `demo-app`/`itemId` 这类中性名；内部标识扫描在 CI 内联执行，模式清单经仓库变量 `PUBSOFT_FORBIDDEN_PATTERNS` / `PUBSOFT_FORBIDDEN_PATTERNS_EXCLUDING_TESTS`（后者跳过 `tests/`）注入——清单不进仓库；本地同名脚本（gitignored）读同样变量
- **金额**统一 `decimal` + `HasPrecision(18, 2)`；主键 `long` Snowflake + `ValueGenerated.Never`
- **客户端命名**：`XxxClientSdk` / `XxxClient`（薄 HTTP 封装，Bearer/`X-Client-*` 由调用方注入）

## 打包

- 版本集中 `src/Directory.Build.props`（`PubSoftVersion`）
- 本地验证：`dotnet pack` → `/tmp/pack/` 或消费方 `localfeed/`（pack 时 `VerifyNuspecDependencies` 自检 nuspec 依赖 == 声明集，不一致即 fail）
- 发布前跑 `tests/PubSoft.PackageConsumerSmoke` + `tests/PubSoft.SecurityConsumerSmoke`

## 密钥管理

- `Jwt:SecretKey` 是宿主侧共享的机器凭证签章密钥示例配置项（`caller_type=Machine` 语义由消费方定义）
- client 边缘入口凭证（clientId→signKey）由宿主实现 `IEdgeSignKeyProvider` 提供——注册表、轮换与下发是宿主私有实现细节
- `appsettings.json` 只放 dev 默认值（`dev-*-change-me`），生产密钥一律走 env/secret 管理

## Push 门禁（强制）

- **push 前必须跑 `bash scripts/verify-before-push.sh`** —— 与 CI 逐步骤对齐（format → ledger → build → tests），全绿才推；本地没有的 CI 步骤一律视为隐形门槛，新增 CI gate 必须同步进该脚本。
- **push 频率收敛**：按会话/主题批量推，不逐节点推——主干上成串红 CI 是事故不是流程。
