---
purpose: 权限 client scope 语义与执行
audience: 集成宿主与评审者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# 权限 Client Scope

[English](permission-client-scope.md) | **简体中文**

`ClientCode` 是租户内的授权命名空间。空字符串为历史/默认命名空间。定义、角色、主体-角色指派、EF 授权解析与 ASP.NET 要求标注必须使用完全相同的 client code。角色授权挂在角色 ID 上，其命名空间由角色决定。

非空 client scope 要求 provider 实现 client-aware 的 `IPermissionGrantProvider.GetGrantsAsync` 重载；接口的兼容实现对非空 code 返回空授权。EF provider 按精确租户+client code 过滤指派与启用角色。`AssignSubjectRolesAsync(subjectId, tenantId, clientCode, roleIds)` 只替换该 client 下的指派；旧重载作用于空 client。

权限要求可显式携带 code，例如：

```csharp
[RequirePermission("records", PermissionFlags.Query, "shop")]
```

标注值是配置而非身份证明。宿主必须从可信路由/配置或已验证的 client 身份选择它，并独立授权权限管理端点。禁止将任意请求参数当作授权命名空间。

`PermissionModule` 是可选的描述性元数据，供管理界面使用；授权评估器不读取它，它也不能授予权限。应用 `ConfigurePubSoftPermissionGrants()` 时该实体被映射。

## 存量数据库升级

库不为该 schema 变更内置迁移——消费方宿主须按其数据库 provider 生成并应用迁移后再部署新授权代码。切换期间不要新旧授权实例混跑：旧 provider 二进制的角色查找不受 client 约束。

1. 为 `pubsoft_permission_role` 与 `pubsoft_subject_permission_role` 增加非空 `ClientCode` 列（长度 64、空串库默认值）。存量角色回填空串；每条指派回填其引用角色的 `ClientCode`（纯旧装全为空串）。
2. 若旧角色需迁入具名 client，从可信部署/业务数据建立角色→client 映射，然后把每条指派的 code 置为引用角色的 code。禁止按角色名猜映射。留在空命名空间的行不会对非空 code 的要求授权。
3. 角色唯一索引 `(TenantId, Name)` 换成 `(TenantId, ClientCode, Name)`；指派唯一/索引定义换成 `(TenantId, ClientCode, RoleId, SubjectId)` 唯一与 `(TenantId, ClientCode, SubjectId)` 非唯一。
4. 启用模块元数据持久化时创建 `pubsoft_permission_module`（按模型配置长度，唯一键 `(TenantId, ClientCode, ModuleKey)`）。它仅是展示/管理数据。
5. 迁移后部署新二进制，配置可信 client 解析，在宿主集成环境验证跨 client 拒绝与指派替换（含空角色列表）。

EF 模型为角色/指派的 `ClientCode` 设空串默认值，省略新列的旧插入继续落在默认命名空间。这不能让混版本授权变得安全，也不推断存量数据应归属哪个非空 client。
