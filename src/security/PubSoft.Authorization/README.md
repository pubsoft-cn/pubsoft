---
purpose: package-level hosting guide for PubSoft.Authorization
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Authorization

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/security/PubSoft.Authorization/README.zh-CN.md)


`PubSoft.Authorization` provides framework-independent permission metadata, lookup, and evaluation against host-supplied grants. It does not authenticate callers, store grants, or resolve user roles. `PubSoft.AspNetCore` adds an opt-in Minimal API endpoint filter; it is not global middleware and does not enforce MVC controllers.

## Define and Register Permissions

Implement `IPermissionDefinitionProvider` and pass providers to `PermissionDefinitionRegistry` at application composition time:

```csharp
using PubSoft.Authorization;

public sealed class UserPermissions : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> GetDefinitions() =>
    [
        new PermissionDefinition
        {
            ModuleKey = "users",
            Flags = PermissionFlags.Query | PermissionFlags.Create,
            DisplayName = "Users",
            ApiPaths = ["GET /api/users", "POST /api/users"],
            FrontendRoutes = ["/users", "/users/:userId"]
        }
    ];
}

var catalog = new PermissionDefinitionRegistry([new UserPermissions()]);
var matches = catalog.GetByApiPath("GET", "/api/users");
```

The registry validates module keys, non-zero known flag bits, extension metadata, duplicate module/flag keys, path syntax, and duplicate paths, then snapshots definitions and route collections. API patterns use `METHOD /path`; frontend and API templates accept a whole path segment as `:name` or `{name}`. Lookup is segment-count exact, ignores HTTP method casing and trailing path slashes, and compares literal path segments ordinally. Query strings and fragments are not part of lookup paths.

`PermissionModuleRegistry` aggregates module providers and resolves display names/descriptions. Metadata attributes such as `PermissionModuleAttribute`, `RequirePermissionAttribute`, `RequireCustomPermissionAttribute`, `RequireExtPermissionAttribute`, and `PermissionExemptAttribute` are framework-independent annotations; this package does not scan assemblies or enforce them.

## Opt-In Minimal API Enforcement

Register the trusted subject accessor and host-owned grant/context services, then apply `AddPubSoftPermissionAuthorization()` to a route handler or route group. Only endpoints with `RequirePermissionAttribute`, `RequireCustomPermissionAttribute`, or `RequireExtPermissionAttribute` metadata are evaluated:

```csharp
app.MapGet("/api/users", ListUsers)
    .WithMetadata(new RequirePermissionAttribute("users", PermissionFlags.Query))
    .AddPubSoftPermissionAuthorization();
```

The filter returns 401 if no subject resolves from the configured trusted issuer, 403 if the resolved tenant does not exactly match `ITenantContext.TenantId`, and 403 if any declared requirement is not granted. On success it continues to the handler. Endpoints without permission metadata continue unchanged. `PermissionExemptAttribute` and `IAllowAnonymous` take precedence over requirement metadata in both the endpoint filter and the MVC filter — an exempt endpoint passes without permission checks even if requirement attributes are also present. Endpoints without any requirement metadata continue unchanged. Authentication middleware, tenant selection/authorization, permission definitions, and grant provider registration remain host responsibilities.

## Explicit Grant Evaluation

Implement `IPermissionGrantProvider` to return the subject's effective grants for the exact subject and tenant scope. Role membership, grant aggregation, and storage remain in that provider:

```csharp
var evaluator = new PermissionAuthorizationService(catalog, grantProvider);
bool allowed = await evaluator.IsGrantedAsync(
    subjectId: 42,
    tenantId: 7,
    moduleKey: "users",
    requiredFlags: PermissionFlags.Query,
    cancellationToken);
```

`PermissionGrant` snapshots one module's standard flags, extension bitmask, and custom permission names. `IsGrantedAsync`, `IsCustomPermissionGrantedAsync`, and `IsExtensionPermissionGrantedAsync` all require a known module/permission declaration and explicit matching grants. Grants are merged only for the exact module and exact nullable tenant scope; host grants do not flow into tenant scope, nor tenant grants into host scope. Standard requirements require every requested bit. Custom names compare ordinally; extension requirements require every requested bit to be declared and granted. Invalid subject/tenant IDs and unknown or undeclared permissions return `false`; provider exceptions propagate and are never treated as an allow.

There is no implicit administrator bypass. The provider must return effective grants according to the host's explicit role/subject assignment rules; callers must pass a subject and tenant scope established by a trusted authentication/tenant boundary.

## Derived Requirements

`PermissionRequirementDeriver` can map HTTP methods and POST action-name words to a `PermissionFlags` value, and can resolve a module from an explicit key, a longest route-prefix map, or a unique controller-name suffix. These are catalog conveniences with heuristic inputs. Pin module keys and requirements explicitly where the application needs stable semantics; do not rely on name derivation as a security boundary.

Permission grant persistence/replacement transactions, role membership, anonymous endpoint behavior, and policy precedence remain host decisions. The evaluator has no admin bypass and does not decide tenant-switch authorization. The Minimal API filter enforces only explicit permission metadata and does not infer requirements from routes or controllers.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Authorization`: `AutoPermissionOptions` `IPermissionDefinitionProvider` `IPermissionGrantProvider` `IPermissionModuleProvider` `PermissionAuthorizationService` `PermissionDefinition` `PermissionDefinitionRegistry` `PermissionExemptAttribute` `PermissionFlags` `PermissionGrant` `PermissionModuleAttribute` `PermissionModuleRegistry` `PermissionRequirementDeriver` `RequireCustomPermissionAttribute` `RequireExtPermissionAttribute` `RequirePermissionAttribute`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**(options & records)**

`ApiPaths`, `AvailableFlags`, `ClientCode`, `CustomPermissions`, `CustomPermissionsExtractor`, `Description`, `DisplayName`, `EnforceDerived`, `ExtFlag`, `Flags`, `FrontendRoutes`, `IsExtPermission`, `ModuleKey`, `ModuleKeyResolver`, `PinAvailableFlagsResolver`, `PinDescriptionResolver`, `PinDisplayNameResolver`, `PinSortOrderResolver`, `SortOrder`

<!-- pubsoft:api-surface:end -->

