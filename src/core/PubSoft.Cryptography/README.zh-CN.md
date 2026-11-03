# PubSoft.Cryptography

[English](README.md) | **简体中文**

带密钥轮换支持的版本化 AEAD 加密——无包依赖。

## Envelope 加密

```csharp
var ring = new MyKeyRing();                      // 宿主实现 IEncryptionKeyRing
var encryptor = new AesGcmEncryptor(ring, timeProvider);

var context = Encoding.UTF8.GetBytes($"{tenantId}:credential:{id}");
var ciphertext = encryptor.Encrypt(secret, context);   // pubsoft:v1:{keyId}:{nonce}:{data}:{tag}
var plaintext = encryptor.Decrypt(ciphertext, context);
```

- AES-256-GCM，随机 96-bit nonce、128-bit tag；AAD 绑定格式版本 + key id + 调用方 context
- key id 支持轮换：用当前 ring key 加密，解密按 envelope `keyId` 解析
- context 不匹配、密文/tag 被篡改、包封格式错误、密钥非 32 字节——全部 fail closed
- `IEncryptionKeyRing` 是集成契约——用你的 KMS/secret manager 实现；无内置默认密钥

## 旧凭据迁移

`CredentialEncryptionService` 恒写 AEAD；仅当构造时显式提供旧密钥才能读旧 AES-CBC 格式。读取结果携带 `RequiresReEncryption` + `ReplacementCiphertext`——在宿主事务中持久化替代密文，服务不自行写存储。

细节与约束见[安全与密码学](../../../docs/security-cryptography.zh-CN.md)。

## SM3 / SM4（国密）

纯托管实现——无 BouncyCastle 依赖，AOT 安全：

```csharp
Sm3.HashHex("abc");                          // 66c7f0f4…8f4ba8e0（GB/T 32905 向量）
Sm3.HmacBase64(key, data);                   // HMAC-SM3 渠道签名
Sm4.EncryptEcbHex(hexKey, plaintext);        // SM4-ECB + PKCS7 → 小写 hex
Sm4.DecryptEcbHex(hexKey, cipherHex);
```

SM4-ECB 仅用于**存量互通**（银联/政务类协议）——ECB 会泄露明文分组模式，自行设计的加密场景一律用 `AesEncryptor`/AES-GCM。`DecryptEcb` 全量校验填充，非法输入抛 `CryptographicException`。

## ClientSigner — HMAC-SHA256 请求签名

签名串格式 `{method}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}`，签名 = `Base64(HMAC-SHA256(secret, 签名串))`。

- `Sign`/`Verify` — 签名计算与常量时间比较
- `ComputeBodyHash` — 请求体 SHA-256 hex（无 body 为空串）
- `IsTimestampValid` — 校验时间戳在 ±5 分钟内；**timestamp 为 Unix 毫秒**
- `TimestampTolerance`/`NonceTtl` — 共享常量（5 分钟 / 10 分钟）

防重放**故意不含**：`ClientSigner` 不存 nonce。宿主须自行记录已用 nonce（如共享缓存以 nonce
为键、TTL=`NonceTtl`）并拒绝重复——`NonceTtl` 覆盖时间窗，过期签名无法重放。

约定 header：`X-Client-Id`、`X-Timestamp`、`X-Nonce`、`X-Signature`。
