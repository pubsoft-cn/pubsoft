# Permission Client Scope

**English** | [简体中文](permission-client-scope.zh-CN.md)

`ClientCode` is an authorization namespace within a tenant. The empty string is the legacy/default namespace. Definitions, roles, subject-role assignments, EF grant resolution, and ASP.NET requirements must use the same exact client code. Role grants remain attached to a role ID, so their namespace is determined by the role.

Non-empty client scopes require a provider implementing the client-aware `IPermissionGrantProvider.GetGrantsAsync` overload. The interface's compatibility implementation returns no grants for non-empty codes. The EF provider filters assignments and enabled roles by exact tenant and client code. `AssignSubjectRolesAsync(subjectId, tenantId, clientCode, roleIds)` only replaces assignments in that client; its legacy overload targets the empty client.

Permission requirements can carry a code explicitly, for example:

```csharp
[RequirePermission("records", PermissionFlags.Query, "shop")]
```

The attribute value is configuration, not identity proof. Hosts must select it from trusted routing/configuration or a validated client identity, and must authorize permission-management endpoints independently. Do not accept an arbitrary request parameter as the authorization namespace.

`PermissionModule` is optional descriptive metadata for management interfaces. It is not read by the authorization evaluator and cannot grant permissions. The entity is mapped when `ConfigurePubSoftPermissionGrants()` is applied to the model.

## Existing Database Upgrade

The library has no built-in migration for this schema change. Generate and apply a migration in the consuming host for its database provider before deploying the new authorization code. Do not run old and new authorization instances concurrently during cutover: old provider binaries do not constrain role lookup by client.

1. Add non-null `ClientCode` columns (length 64) to `pubsoft_permission_role` and `pubsoft_subject_permission_role`, with an empty-string database default. Backfill existing roles to the empty string. Backfill each assignment from its referenced role's `ClientCode`; for a legacy-only installation this is also empty.
2. If old roles need to move into named clients, map each role to its owning client from trusted deployment/business data, then set every assignment's code to the referenced role's code. Do not guess mappings from role names. Rows left in the empty namespace will not authorize requirements using a non-empty code.
3. Replace the role unique index `(TenantId, Name)` with `(TenantId, ClientCode, Name)`. Replace the assignment uniqueness/index definitions with `(TenantId, ClientCode, RoleId, SubjectId)` unique and `(TenantId, ClientCode, SubjectId)` non-unique.
4. Create `pubsoft_permission_module` with the model's configured lengths and unique key `(TenantId, ClientCode, ModuleKey)` if module metadata persistence is enabled. Preserve it as display/management data only.
5. Deploy the new binaries after migration, configure trusted client resolution, and verify cross-client denial plus assignment replacement (including an empty role list) in the host's integration environment.

The EF model sets empty-string defaults for role and assignment `ClientCode` so legacy inserts that omit the new columns continue to represent the default namespace. This does not make mixed-version authorization safe, nor does it infer which non-empty client should own existing data.
