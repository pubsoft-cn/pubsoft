---
purpose: package-level hosting guide for PubSoft.Cryptography
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Cryptography

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/core/PubSoft.Cryptography/README.zh-CN.md)

Versioned AEAD encryption with key rotation support — no package dependencies.

## Envelope encryption

```csharp
var ring = new ConfigEncryptionKeyRing(options);  // built-in config-driven keyring — or implement IEncryptionKeyRing over a KMS
var encryptor = new AesGcmEncryptor(ring, timeProvider);

var context = Encoding.UTF8.GetBytes($"{tenantId}:credential:{id}");
var ciphertext = encryptor.Encrypt(secret, context);   // pubsoft:v1:{keyId}:{nonce}:{data}:{tag}
var plaintext = encryptor.Decrypt(ciphertext, context);
```

- AES-256-GCM, random 96-bit nonce, 128-bit tag; AAD binds format version + key id + caller context
- Key ids enable rotation: encrypt with the current ring key, decrypt resolves by envelope `keyId`
- Mismatched context, tampered ciphertext/tag, malformed envelope, or non-32-byte key all fail closed
- `IEncryptionKeyRing` is an integration contract — the built-in `ConfigEncryptionKeyRing` resolves `EncryptionKeyRingOptions` (`ActiveKeyId` + `Keys:{id}=base64`); production hosts should still implement the contract over a KMS/secret manager

## Legacy credential migration

`CredentialEncryptionService` always writes AEAD and can read the old AES-CBC format only when constructed with the explicit legacy key. Read results carry `RequiresReEncryption` + `ReplacementCiphertext` — persist the replacement in the host's transaction; the service never writes storage itself.

Details and constraints: [Security and Cryptography](../../../docs/security-cryptography.md).

## SM3 / SM4 (GB/T national crypto)

Pure-managed implementations — no BouncyCastle dependency, AOT-safe:

```csharp
Sm3.HashHex("abc");                          // 66c7f0f4…8f4ba8e0 (GB/T 32905 vector)
Sm3.HmacBase64(key, data);                   // HMAC-SM3 for channel signing
Sm4.EncryptEcbHex(hexKey, plaintext);        // SM4-ECB + PKCS7 → lower hex
Sm4.DecryptEcbHex(hexKey, cipherHex);
```

SM4-ECB is provided for **interop only** (legacy CUP/government-style integrations) — ECB leaks plaintext block patterns; use `AesEncryptor`/AES-GCM for anything you design yourself. `DecryptEcb` validates the full padding and throws `CryptographicException` on malformed input.

## ClientSigner — HMAC-SHA256 request signing

Signs requests as `Base64(HMAC-SHA256(secret, "{method}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}"))`.

- `Sign`/`Verify` — signature computation and constant-time comparison
- `ComputeBodyHash` — SHA-256 hex of the request body (empty string for no body)
- `IsTimestampValid` — checks the timestamp is within ±5 minutes; **timestamps are Unix milliseconds**
- `TimestampTolerance`/`NonceTtl` — shared constants (5 min / 10 min)

Replay protection is intentionally NOT included: `ClientSigner` does not store nonces. The host must
record used nonces (e.g. in a shared cache keyed by nonce, TTL = `NonceTtl`) and reject repeats —
`NonceTtl` covers the timestamp window so an old signature cannot be replayed after expiry.

Headers used by convention: `X-Client-Id`, `X-Timestamp`, `X-Nonce`, `X-Signature`.

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftCredentialEncryption` |

**(options & records)**

`ActiveKeyId`, `HelpLink`, `HResult`, `Plaintext`, `ReplacementCiphertext`, `RequiresReEncryption`, `Source`

<!-- pubsoft:api-surface:end -->
