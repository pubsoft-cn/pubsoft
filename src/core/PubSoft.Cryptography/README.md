# PubSoft.Cryptography

**English** | [简体中文](README.zh-CN.md)

Versioned AEAD encryption with key rotation support — no package dependencies.

## Envelope encryption

```csharp
var ring = new MyKeyRing();                      // host implements IEncryptionKeyRing
var encryptor = new AesGcmEncryptor(ring, timeProvider);

var context = Encoding.UTF8.GetBytes($"{tenantId}:credential:{id}");
var ciphertext = encryptor.Encrypt(secret, context);   // pubsoft:v1:{keyId}:{nonce}:{data}:{tag}
var plaintext = encryptor.Decrypt(ciphertext, context);
```

- AES-256-GCM, random 96-bit nonce, 128-bit tag; AAD binds format version + key id + caller context
- Key ids enable rotation: encrypt with the current ring key, decrypt resolves by envelope `keyId`
- Mismatched context, tampered ciphertext/tag, malformed envelope, or non-32-byte key all fail closed
- `IEncryptionKeyRing` is an integration contract — implement it over your KMS/secret manager; there are no default keys

## Legacy credential migration

`CredentialEncryptionService` always writes AEAD and can read the old AES-CBC format only when constructed with the explicit legacy key. Read results carry `RequiresReEncryption` + `ReplacementCiphertext` — persist the replacement in the host's transaction; the service never writes storage itself.

Details and constraints: [Security and Cryptography](../../../docs/security-cryptography.md).
