# 贡献指南

欢迎参与 PubSoft——PR、issue、讨论都接受。

## 流程

1. 超过拼写级别的改动**先开 issue**——方向对齐再投入。
2. Fork 后基于 `main` 开主题分支，一个 PR 只装一个逻辑变更。
3. 提交信息 `type: summary`（`feat:`/`fix:`/`tests:`/`ci:`/`tools:`/`lift:`/`security:`/`docs:`/`build:`），正文写 *why*。一个 commit 一个完整语义节点，不凑数。
4. 推送前跑 `bash scripts/verify-before-push.sh`——与 CI 逐步骤对齐（format → 台账 → 构建 → 测试），过不了关的 PR 不会合并。
5. 对 `main` 开 PR 并填模板——评审会回复。

## 会卡人的规则

- 新公共符号必须入 `PublicAPI.Unshipped.txt`（`bash scripts/sync-public-api.sh`）——analyzer 与 CI 双重强制。
- 文档双语：`README.md`/`docs/*.md` 必须配 `.zh-CN.md`。
- 新包按 `docs/documentation-governance.md`——模板、front-matter、挂号行、测试。
- 不提交密钥、内部标识、客户数据——CI 有扫描。
- 带已知漏洞公告的依赖在 restore 即失败（NU1903/NU1904 是 error）——选已修补版本。

## 评审预期

- 评审看语义完整性、测试覆盖、边界诚实度——风格由 `dotnet format` 管。
- 合并走 squash 或作者 rebase，保留有意义的历史。

## 安全

漏洞请走 [SECURITY.md](SECURITY.md)——不要开公开 issue。
