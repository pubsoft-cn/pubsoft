# Security and Cryptography

**English** | [简体中文](security-cryptography.zh-CN.md)

This document describes the current generic security packages. These APIs do not provide a substitute for threat modeling, secret management, or application-specific authorization.

## Package Boundaries

- `PubSoft.Security` contains password hashing, PII masking, HMAC signature validation, and a bounded in-process rate limiter. It has no package dependencies.
- `PubSoft.Cryptography` contains AES-GCM encryption and the credential CBC migration reader. It has no package dependencies.
- `PubSoft.AspNetCore` contains request middleware and security response headers. It depends on `PubSoft.Security` and the ASP.NET Core shared framework; it is optional.

## AEAD Envelope

`AesGcmEncryptor` writes this format:

```text
pubsoft:v1:{keyId}:{nonceBase64}:{ciphertextBase64}:{tagBase64}
```

The algorithm is AES-256-GCM with a random 96-bit nonce and a 128-bit authentication tag. The authenticated data binds the format version and key id. Callers can also bind application context, such as a stable tenant and record identifier:

```csharp
var context = Encoding.UTF8.GetBytes($"{tenantId}:credential:{credentialId}");
var encrypted = encryptor.Encrypt(secret, context);
var plaintext = encryptor.Decrypt(encrypted, context);
```

The same context bytes are required for decryption. A mismatched context, modified ciphertext/tag, malformed envelope, unavailable key, or key that is not exactly 32 bytes fails closed. Use a unique key id for each key version and retain old keys in the key ring until their ciphertexts have been rotated. Never derive encryption keys from passwords or use a built-in default key.

`IEncryptionKeyRing` is an integration contract, not a secret store. Applications must implement it using their approved secret manager or key-management service. The `MemoryNonceStore`/test key ring examples are not substitutes for production key storage.

## Legacy Credential Migration

`CredentialEncryptionService` always writes the AEAD format. It can read the old source format only when constructed with the exact legacy key material used by the old deployment. The key is required explicitly; the former development fallback is not supported. The old format is AES-CBC with PKCS7 padding, a 16-byte IV prefixed to the ciphertext, then Base64 encoding. It has no authentication tag and is malleable, so only use the migration reader on trusted stored values inside a controlled migration/login path.

When a legacy value is read, the result contains `RequiresReEncryption = true` and `ReplacementCiphertext`. Persist that replacement with the record update in the application's transaction. The service does not update storage itself. Supply the same associated-data bytes when encrypting/decrypting current AEAD values; legacy CBC values did not authenticate associated data.

Do not bulk rewrite records unless the old key and ciphertext format are verified against a backup. Keep an auditable count of migrated, unreadable, and not-yet-migrated records. Do not log plaintext or key material.

## Passwords and PII

`PasswordHasher` uses PBKDF2-HMAC-SHA256 with a random 16-byte salt and 600,000 default iterations. `VerifyAndRehash` verifies a current or supported legacy unsalted SHA-256 hash and returns an optional replacement hash when verification succeeds but the stored format/work factor needs upgrading:

```csharp
var verification = PasswordHasher.VerifyAndRehash(password, storedHash);
bool passwordIsValid = verification.Succeeded;
string? replacementHash = verification.ReplacementHash;
```

Persist the replacement only after successful verification, preferably with the sign-in state update in the same host-owned transaction. This helper does not load users, update a security stamp, count failed attempts, or define lockout rules. Stored iteration counts are bounded to prevent attacker-controlled excessive work. Password hashes are not encryption keys.

`DataMaskingService` is intended for logs, diagnostics, and presentation. It is not anonymization and must not replace access control or data minimization. Visible-length options are validated and snapshotted at construction; if a configured visible region would expose the entire short input, that input is masked instead.

## HMAC Replay Protection

`HmacSignatureValidator` signs `method`, `path`, `timestamp`, `nonce`, and optionally `bodyHash` separated by newlines. HMAC-SHA256 signatures are 64 hexadecimal characters and compared in constant time. The nonce is reserved only after signature verification, and the store must atomically allow one use per app id/nonce until the signed timestamp expires.

`MemoryNonceStore` is bounded and process-local. Capacity exhaustion rejects new reservations. It does not prevent replays across multiple application instances. Multi-instance applications must register an `INonceStore` backed by an atomic distributed operation with expiry (for example, an atomic set-if-absent), and must fail closed when that store is unavailable.

## Rate Limiting and ASP.NET Core

`RateLimiter` is a bounded, process-local fixed-window limiter with atomic per-identifier counts. It is not cluster-wide; use an external gateway or a separately reviewed distributed implementation for shared quotas. Invalid identifiers and capacity exhaustion fail closed.

`PubSoft.AspNetCore` HMAC middleware protects every path by default; excluded prefixes are opt-in. Applications must register `IAppSecretResolver`. Body hashing is opt-in and capped at 16 MiB. The default nonce store is process-local and should be replaced for multi-instance deployments. Security headers are enabled by default; HSTS is emitted only for HTTPS unless explicitly enabled for development.

## Verification

Run:

```bash
dotnet test PubSoft.sln
bash scripts/verify-package-consumer.sh
```

Security tests cover AEAD tampering/key rotation/context binding, legacy CBC read-and-reencrypt, malformed signatures/replay concurrency, limiter concurrency/capacity, and ASP.NET middleware behavior. These tests do not validate a production secret manager or a distributed nonce store implementation; those require deployment-specific integration tests.
