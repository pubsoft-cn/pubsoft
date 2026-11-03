# 打包与发布工程

[English](packaging.md) | **简体中文**

## 版本与许可

- `Directory.Build.props` 统一 `PubSoftVersion`（当前 `0.1.0-alpha.10`），各 csproj 不单独写 `<Version>`
- 许可证：MIT（根 `LICENSE` + `PackageLicenseExpression`）；安全联系：pubsoft@gmail.com（`SECURITY.md`）
- 每包 `src/*/PubSoft.*/README.md` 随 nupkg 发布（`PackageReadmeFile`），模块级宿主指南就近维护

## API 基线

- 全项目启用 `Microsoft.CodeAnalysis.PublicApiAnalyzers`（`src/Directory.Build.props`）
- 每包 `PublicAPI.Shipped.txt`（已发布 API）+ `PublicAPI.Unshipped.txt`（未发布新增）；新增公共符号必须播种，否则 RS0016 警告
- `RS0026`（可选参数重载）为刻意的 API 风格，项目级 NoWarn
- 正式发布后将 `Unshipped` 内容并入 `Shipped` 形成 API 兼容基线

## 依赖治理

- 包依赖一致性：`src/Directory.Build.targets` 在打包时解析 nupkg 的 nuspec 并校验声明依赖与项目/包引用一致，`scripts/sync-package-deps.py` 重新生成 nuspec 元数据；这是一致性校验，不是审批白名单
- 每包有独立隔离 consumer（`tests/PubSoft.{X}ConsumerSmoke`，游离于解决方案外）：仅直接引用对应包 + 宿主显式依赖，由 `scripts/verify-package-consumer.sh` 基于本地新打包 feed 验证
- 本地交付：`dotnet pack` → 本地 feed；不下发浮版本（`latest`/`*`），新依赖选发布 ≥7 天的版本

## AOT 验证

- 叶子包见 [design-principles.md](design-principles.zh-CN.md) AOT 策略；`scripts/verify-aot-consumer.sh` 跑真实 `PublishAot` 原生发布+执行（无工具链优雅跳过，已进 CI）
- ⚠️ 增量 `PublishAot` 可能复用陈旧 native 链接——换包后必须 clean 重发验证

## CI（`.github/workflows/ci.yml`）

restore → Release 构建 → 全量测试（Redis + MySQL 服务可用时跑集成）→ format 校验 → 打包 → 隔离 consumer → AOT smoke → gitleaks secret scan（`fetch-depth: 0`）→ 漏洞列表。高危/严重 NuGet 告警经 `NU1903`/`NU1904` warnings-as-errors 在 restore 阶段即失败。CI 不含 publish 步骤——发布需单独授权管线。

## 环境门控测试

`PUBSOFT_REDIS` / `PUBSOFT_MYSQL` 未设置时对应集成测试（`[RedisFact]`/`[MySql84Fact]`）自动跳过；覆盖 provider 只保证测过的 Redis/MySQL 场景。

## 发布阶段

- **内部验证**：`$(PubSoftVersion)` 带 `-alpha.N` 后缀。打包到 feed 目录，经消费方 `local-dev` NuGet 源接入；prerelease 包不作为稳定版出现，业务验证不会占用正式发布所需版本号。
- **公开发布**：去掉 prerelease 后缀、提升稳定版本号，仓库转公开后发往 NuGet.org。`RepositoryUrl` 已嵌入包元数据。
