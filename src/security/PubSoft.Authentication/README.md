# PubSoft.Authentication

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Authentication` provides a framework-neutral immutable snapshot of a `ClaimsPrincipal`. It copies the principal's authenticated flag and each claim's type, value, value type, and issuer:

```csharp
using PubSoft.Authentication;
using System.Security.Claims;

ClaimsPrincipal principal = GetPrincipal();
PrincipalSnapshot snapshot = PrincipalSnapshot.FromClaimsPrincipal(principal);

foreach (PrincipalClaim claim in snapshot.Claims)
    Console.WriteLine($"{claim.Type}={claim.Value} ({claim.Issuer})");
```

The snapshot preserves claim order and duplicates across the principal's identities, and copies claims into a read-only collection. `IsAuthenticated` is true when any identity on the source principal is authenticated. The conversion rejects a null principal.

## Subject Resolution

The snapshot deliberately does not interpret claim names or values. It does not resolve a user ID, tenant, role, permission, administrator status, or trusted issuer. Consumers must use an explicitly defined identity/authorization contract; a raw claim is not by itself proof that a tenant switch or privileged operation is allowed.

For applications that have already defined the authoritative claim mapping, `AuthenticatedSubjectResolver` can map a configured subject claim and optional tenant claim from one exact trusted issuer:

```csharp
var resolver = new AuthenticatedSubjectResolver(
    subjectClaimType: "sub",
    trustedIssuer: "https://identity.example",
    tenantClaimType: "tenant_id");

AuthenticatedSubject? subject = resolver.Resolve(snapshot);
if (subject is null)
    throw new UnauthorizedAccessException();
```

Resolution requires an authenticated snapshot, exactly one matching subject claim with a positive invariant-culture `long` value, and at most one matching tenant claim. A present tenant claim must also be a positive `long`; otherwise resolution fails closed. An absent configured tenant claim produces `TenantId = null`. Other issuers and claim names are ignored. Configure the trusted issuer and claim types from the host's verified token contract; this helper does not validate the token, authorize tenant switching, resolve roles, or grant permissions.

## Scoped Accessors

`IPrincipalSnapshotAccessor` is the framework-neutral read contract. The optional `PubSoft.AspNetCore` integration registers a scoped accessor that snapshots the current `HttpContext.User` on each call and returns `null` when there is no current request. It does not cache a snapshot for the scope or alter the principal.

To resolve the configured subject directly per HTTP request, register the additional scoped accessor explicitly:

```csharp
builder.Services.AddAuthenticatedSubjectAccessor(
    subjectClaimType: "sub",
    trustedIssuer: "https://identity.example",
    tenantClaimType: "tenant_id");

var subject = httpContext.RequestServices
    .GetRequiredService<IAuthenticatedSubjectAccessor>()
    .GetCurrent();
```

This registration also adds the principal snapshot accessor and a singleton resolver. Configure it once during service registration. A `null` result means no current request or a rejected mapping; it does not mean an endpoint response code or tenant-switch decision. The host still owns authentication middleware ordering, issuer validation, and authorization of tenant selection.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Authentication`: `AuthenticatedSubject` `AuthenticatedSubjectResolver` `IAuthenticatedSubjectAccessor` `IPrincipalSnapshotAccessor` `PrincipalClaim` `PrincipalSnapshot`
