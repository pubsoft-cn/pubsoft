# PubSoft.Security

**English** | [简体中文](README.zh-CN.md)

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

`HmacSignatureValidator` verifies `method`/`path`/`timestamp`/`nonce`/`bodyHash` signatures with constant-time comparison. The nonce is reserved via `INonceStore` **only after** the signature verifies; `MemoryNonceStore` is bounded and process-local — multi-instance deployments register a distributed implementation (`PubSoft.Security.Redis`).

## Rate Limiting

`RateLimiter` is a bounded single-process fixed-window limiter with atomic per-identifier counts. Not cluster-wide — use a gateway or reviewed distributed implementation for shared quotas. Invalid identifiers and capacity exhaustion fail closed.

## Boundaries

Invalid input, exhausted capacity, or unavailable stores all fail closed. Keys and credentials never appear in logs or exceptions. ASP.NET Core middleware surfaces (HMAC middleware, IP rate limiting, security headers) live in `PubSoft.AspNetCore`. Security model details: [docs/security-cryptography.md](../../../docs/security-cryptography.md).

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Security`: `DataMaskingService` `HmacSignatureValidator` `INonceStore` `MemoryNonceStore` `PasswordHasher` `PasswordVerification` `RateLimiter` `RateLimitOptions` `RateLimitResult`
