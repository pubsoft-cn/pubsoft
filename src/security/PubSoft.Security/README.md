---
purpose: package-level hosting guide for PubSoft.Security
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Security

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/security/PubSoft.Security/README.zh-CN.md)

Generic security primitives, zero package dependencies — password hashing, PII masking, HMAC replay protection, and a bounded in-process rate limiter.

## Password Hashing

```csharp
var hash = PasswordHasher.Hash(password);                 // PBKDF2-HMAC-SHA256, random salt
var result = PasswordHasher.VerifyAndRehash(password, storedHash);
if (result.Succeeded && result.ReplacementHash is { } next)
    SaveUpgradedHash(next);                               // host persists in its own transaction
```

PBKDF2-HMAC-SHA256 with a random 16-byte salt and 600,000 default iterations. `VerifyAndRehash` also verifies supported legacy formats (unsalted SHA-256, low-work-factor hashes) and returns `ReplacementHash` so callers can transparently upgrade stored values — persist it in the same transaction as the sign-in update. The helper does not load users, update a security stamp, or count failed attempts.

## PII Masking

`DataMaskingService` masks values for logs and diagnostics with validated visible-length options. It is not anonymization and never substitutes for access control; a configured visible region that would expose an entire short input masks the whole input instead.

## HMAC Replay Protection

`HmacSignatureValidator` verifies `method`/`path+rawQuery`/`timestamp`/`nonce`/`bodyHash` signatures with constant-time comparison. The nonce is reserved via `INonceStore` **only after** the signature verifies; `MemoryNonceStore` is bounded and process-local — multi-instance deployments register a distributed implementation (`PubSoft.Security.Redis`).

## Rate Limiting

`RateLimiter` is a bounded single-process fixed-window limiter with atomic per-identifier counts. Not cluster-wide — use a gateway or reviewed distributed implementation for shared quotas. Invalid identifiers and capacity exhaustion fail closed.

## Refresh Token Families

`RefreshTokenFamilyService` rotates opaque refresh tokens within a family: each presented token is consumed atomically and a successor is issued, keyed by SHA-256 hash — raw tokens never persist. Presenting an already-consumed token is treated as a leak signal and revokes the **entire family** (`RefreshRotationVerdict.ReuseDetected`); revoked families fail closed on any further rotation. Families carry an absolute `FamilyMaxLifetime` deadline independent of per-token `TokenLifetime`; `RevokeSubjectAsync` revokes all known families of a subject (logout everywhere). `AddPubSoftMemoryRefreshTokenFamily` registers the bounded in-process store — single-instance only; register a shared `IRefreshTokenFamilyStore` (e.g. `PubSoft.Security.Redis`) for multi-instance deployments, and note that in-process state does not survive restarts.

## Token Denylist

`ITokenDenylist` is the revocation source of truth: `DenyAsync` records a token id (`TokenDenyIds.ForToken` = SHA-256 of the raw token, or a caller-chosen stable id such as jti) until the token's own expiry, and `IsDeniedAsync` answers per-request revocation checks. Entries self-clean at expiry — the denylist never outlives the tokens it denies. `AddPubSoftMemoryTokenDenylist` registers the bounded in-process implementation — single-instance only; register a shared implementation (e.g. `PubSoft.Security.Redis`) for multi-instance deployments. Note: in-process revocation state does not survive restarts — a revoked access token can revive for its remaining TTL after a restart unless a shared/durable store is used.

## Boundaries

Invalid input, exhausted capacity, or unavailable stores all fail closed. Keys and credentials never appear in logs or exceptions. ASP.NET Core middleware surfaces (HMAC middleware, IP rate limiting, security headers) live in `PubSoft.AspNetCore`. Security model details: [docs/security-cryptography.md](../../../docs/security-cryptography.md).

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Security`: `DataMaskingService` `HmacSignatureValidator` `INonceStore` `MemoryNonceStore` `PasswordHasher` `PasswordVerification` `RateLimiter` `RateLimiterOptions` `RateLimitResult`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftMemoryRefreshTokenFamily` |
| `AddPubSoftMemoryTokenDenylist` |
| `AddPubSoftRefreshTokenFamilyService` |

**(options & records)**

`ActiveIdentifiers`, `AppId`, `BankCardMaskChar`, `BankCardSuffixVisibleDigits`, `Consumed`, `EmailMaskChar`, `EmailPrefixVisibleChars`, `Enabled`, `Error`, `ExpiresAt`, `FamilyExpiresAt`, `FamilyId`, `FamilyMaxLifetime`, `GeneralMaskChar`, `IdCardMaskChar`, `IdCardPrefixVisibleDigits`, `IdCardSuffixVisibleDigits`, `IsAllowed`, `IssuedAt`, `IsValid`, `LimitByIpAddress`, `LimitByUserId`, `MaxAppIdLength`, `MaxIdentifiers`, `MaxNonceLength`, `MaxSubjectLength`, `MaxTimestampSkewSeconds`, `MaxTokenLength`, `NewToken`, `PhoneMaskChar`, `PhoneSuffixVisibleDigits`, `Reason`, `RejectionReason`, `RemainingRequests`, `ReplacementHash`, `RequestsPerWindow`, `ResetTime`, `SignatureEncoding`, `Subject`, `Succeeded`, `TimestampUnit`, `Token`, `TokenExpiresAt`, `TokenHash`, `TokenId`, `TokenLifetime`, `TotalIdentifiers`, `TotalRequests`, `Verdict`, `WindowSize`

<!-- pubsoft:api-surface:end -->

