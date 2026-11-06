---
purpose: package-level hosting guide for PubSoft.Identity
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Identity

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/core/PubSoft.Identity/README.zh-CN.md)


`PubSoft.Identity` contains a framework-neutral user model and a read-only profile contract. Use `IdentityUser` for the basic tenant-aware user aggregate, and `IdentityUserSnapshot` when passing profile data without credentials or entity behavior.

```csharp
using PubSoft.Identity;

public sealed record UserProfile(
    long Id,
    string UserName,
    string Name,
    string? PhoneNumber) : IIdentityUser;

IIdentityUser profile = new UserProfile(42, "jdoe", "Jane Doe", null);
IdentityUserSnapshot snapshot = IdentityUserSnapshot.FromUser(profile);
```

The snapshot copies `Id`, `UserName`, `Name`, and `PhoneNumber` into get-only properties. `FromUser` rejects a null source. The contract does not define validation, normalization, uniqueness, persistence, or lifecycle behavior for these values.

## IdentityUser and Persistence

The profile is credential-free: it has no password, token, or secret fields. It also does not establish authentication, roles, permissions, tenant membership, or authorization policy. Hosts must keep those decisions in their identity and authorization boundaries rather than infer them from this profile.

`IdentityUser` derives from `FullAuditedAggregateRoot`, uses an application-assigned positive ID, and carries the optional `TenantId`, profile fields, `NormalizedUserName`, optional `PasswordHash`, and `SecurityStamp`. Usernames are trimmed and normalized with `ToUpperInvariant`; this gives stores a stable comparison key but does not enforce uniqueness. The optional `PubSoft.EntityFrameworkCore.ConfigurePubSoftIdentityUsers()` mapping provides a unique `(TenantId, NormalizedUserName)` index and uses the base context tenant and soft-delete filters. The index enforces tenant-user uniqueness for non-null tenant IDs. Host-level (`TenantId = null`) uniqueness must be handled explicitly because relational providers differ in how unique indexes treat null values.

`SetPasswordHash` accepts an opaque encoded hash, not a plaintext password; create or verify hashes with `PubSoft.Security.PasswordHasher`. Setting or removing the hash rotates `SecurityStamp`. This is only a model update: a host must persist it and validate the stamp in its token/session policy for it to invalidate existing sessions. The model deliberately has no login-failure counter, lockout duration, external OAuth token storage, or login transaction behavior.

The EF mapping is opt-in and does not create or migrate tables by itself; add `IdentityUser` to the host DbContext and apply `ConfigurePubSoftIdentityUsers()` before generating/applying a provider-specific migration. The mapping stores the opaque password hash but does not implement password verification, lockout, security-stamp token validation, external login bindings, OAuth token storage, or login transactions. Hosts must choose unique-index behavior for host-level users and test that policy on their database provider.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Identity`: `IIdentityUser` `IdentityUser` `IdentityUserSnapshot`
