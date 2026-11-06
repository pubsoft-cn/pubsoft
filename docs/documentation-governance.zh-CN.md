---
purpose: PubSoft 文档分层结构、模板与复审纪律
audience: 贡献者与评审者
owner: PubSoft 维护组
last-reviewed: 2026-11-05
---

# 文档治理

## 三层结构

| 层 | 位置 | 职责 |
|---|---|---|
| 总览 | 根 `README.md` | 项目定位 + 包挂号表——每个可打包 `PubSoft.*` 必须出现 |
| 治理 | `docs/` | 模板、规范（本文件）、安全边界分析、文档索引——规则与横切文档 |
| 包级 | `src/*/PubSoft.*/README.md` | 包级接入指南——与模板节序一致 |

三层全部强制双语（`README.md` + `README.zh-CN.md`）；生成物（覆盖率矩阵、API 导出）豁免 zh 版——生成而非撰写。

## 模板

`docs/templates/package-readme.template.{md,zh-CN.md}` 是新包文档的强制起点。
必备节：范围与保障（信任边界 + fail-closed 默认 + opt-in）、接入、选项、宿主残余责任、公共 API 面（生成标注）。

每篇文档带 front-matter：`purpose / audience / owner / last-reviewed`。

## 复审节奏

- `last-reviewed` 驱动季度复审——过期文档即更即删，不留腐文（living library）。
- 文档归属登记在 `docs/README.md` 索引表——每条目点名 owner。

## 示例代码必须可信

README 代码块是承诺不是装饰——必须与发布的公共 API 一致。方向（取自 Microsoft Learn）：
建 `tests/PubSoft.DocsSnippets` 项目把 README 示例改成可编译引用——计划下个节点落地；
落地前评审以 `PublicAPI.Unshipped.txt` 逐块人工核对。


## 变更触发矩阵

每类变更对应必备工件——同一 commit 节点内全部齐活。
`check-commit.sh` 对可检测行自动校验。

| 触发 | 必备工件（同一 commit 内） |
|---|---|
| 新增包（`src/*/*.csproj` 落盘） | 根 `README.md` 挂号行 · 包 `README.md` + `README.zh-CN.md`（模板起） · `PublicAPI.Unshipped.txt` · `PubSoft.sln` 条目 · `tests/` 覆盖 |
| 新增公共符号 | `PublicAPI.Unshipped.txt` 条目（RS0016 硬门） · 包 README API 面若重新生成则同步 |
| 行为/安全边界变更 | 包 README Options/Guarantees 节 · 涉边界时 `docs/security-boundary-analysis` 行 · 回归测试 |
| 新增 `docs/` 文档 | `.zh-CN.md` 配对 · front-matter · `docs/documentation-governance` 索引行 |
| 删除/重命名包 | 根 README 行移除 · sln 清理 · 台账清理 · 依赖方 README 链接 |
| 版本/发布 | `Directory.Build.props` `PubSoftVersion` · 行为级变更须发 release note |

## 文档索引

| 文档 | Owner | 最近复审 |
|---|---|---|
| `docs/design-principles` | PubSoft 维护组 | — |
| `docs/security-boundary-analysis` | PubSoft 维护组 | — |
| `docs/packaging` | PubSoft 维护组 | — |
| `docs/documentation-governance` | PubSoft 维护组 | 2026-11-05 |
| `docs/templates/package-readme` | PubSoft 维护组 | 2026-11-05 |

## 分析器出处

`tools/PubSoft.Analyzers` 是依赖图中唯一的分析器。性能/用法规则（PUBSOFT004-011）
的规则形态移植自公开开源分析器——Meziantou.Analyzer（MAxxxx）与
dotnet/roslyn-analyzers（CA18xx）——不消费任何外部分析器包。出处在本处与
每条规则的 docstring 中声明；语义实现为自有。
