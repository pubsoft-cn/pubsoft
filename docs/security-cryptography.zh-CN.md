---
purpose: 安全与密码学清单及默认
audience: 集成宿主与评审者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# 安全与密码学

[English](security-cryptography.md) | **简体中文**

本文档描述当前通用安全包。这些 API 不能替代威胁建模、密钥管理或应用级授权设计。

## 包边界

- `PubSoft.Security` 提供密码哈希、PII 脱敏、HMAC 签名验证和有界进程内限流器。无包依赖。
- `PubSoft.Cryptography` 提供 AES-GCM 加密和凭据 CBC 迁移读取器。无包依赖。
- `PubSoft.AspNetCore` 提供请求中间件与安全响应头。依赖 `PubSoft.Security` 与 ASP.NET Core 共享框架，为可选包。

## AEAD 包封格式

`AesGcmEncryptor` 输出格式：

```text
pubsoft:v1:{keyId}:{nonceBase64}:{ciphertextBase64}:{tagBase64}
```

算法为 AES-256-GCM，随机 96-bit nonce + 128-bit 认证 tag。附加认证数据绑定格式版本与 key id；调用方还可绑定应用上下文（如稳定的租户与记录标识）：

```csharp
var context = Encoding.UTF8.GetBytes($"{tenantId}:credential:{credentialId}");
var encrypted = encryptor.Encrypt(secret, context);
var plaintext = encryptor.Decrypt(encrypted, context);
```

解密必须使用相同的 context 字节。context 不匹配、密文/tag 被篡改、包封格式错误、密钥不可用或密钥长度不是 32 字节——全部 fail closed。每个密钥版本使用唯一 key id，在密文轮换完成前将旧密钥保留在 key ring 中。禁止从口令派生加密密钥，禁止内置默认密钥。

`IEncryptionKeyRing` 是集成契约而非密钥库——应用须用经批准的 secret manager/KMS 实现。`MemoryNonceStore`/测试 key ring 样例不是生产级密钥存储。

## 旧凭据迁移

`CredentialEncryptionService` 始终写 AEAD 格式。仅当构造时显式提供旧部署使用的原始密钥材料，才能读取旧源格式；不再支持旧的开发期回退。旧格式为 AES-CBC + PKCS7 + 16 字节 IV 前缀 + Base64，无认证 tag、可塑性可篡改——仅在受控迁移/登录路径对可信存量值使用迁移读取器。

读取旧值时结果含 `RequiresReEncryption = true` 与 `ReplacementCiphertext`——随记录更新在应用事务中持久化替代密文，该服务不自行更新存储。当前 AEAD 值加解密须提供相同 associated-data 字节；旧 CBC 值不认证 associated data。

未经备份验证旧密钥与密文格式前，禁止批量重写记录。迁移/不可读/未迁移记录须保留可审计计数。禁止记录明文或密钥材料。

## 密码与 PII

`PasswordHasher` 使用 PBKDF2-HMAC-SHA256（随机 16 字节盐，默认 600,000 迭代）。`VerifyAndRehash` 验证当前格式或受支持的旧无盐 SHA-256 哈希，验证成功但格式/工作因子需升级时返回替代哈希：

```csharp
var verification = PasswordHasher.VerifyAndRehash(password, storedHash);
bool passwordIsValid = verification.Succeeded;
string? replacementHash = verification.ReplacementHash;
```

仅在验证成功后持久化替代哈希，最好与登录状态更新置于同一宿主事务。该助手不加载用户、不更新 security stamp、不累计失败次数、不定义锁定规则。存储迭代数有上限以防止攻击者控制过高计算量。密码哈希不是加密密钥。

`DataMaskingService` 用于日志、诊断与展示。它不是匿名化手段，不能替代访问控制或数据最小化。可见长度选项在构造时校验并快照；若配置的可见区间会暴露整个短输入，则该输入整体脱敏。

## HMAC 防重放

`HmacSignatureValidator` 以换行分隔签名 `method`/`path`/`timestamp`/`nonce` 及可选 `bodyHash`。HMAC-SHA256 签名为 64 位十六进制字符并做定时常量比较。nonce 仅在签名验证通过后占用，存储须以原子操作保证每 app id/nonce 在签名时间戳过期前只可用一次。

`MemoryNonceStore` 有界且进程内。容量满拒绝新 nonce，不能防跨实例重放。多实例应用必须注册以原子分布式操作（如原子 set-if-absent）加过期实现的 `INonceStore`，且存储不可用时 fail closed。

## 限流与 ASP.NET Core

`RateLimiter` 是有界、进程内、固定窗口限流器，按标识符原子计数。非集群级——共享配额需外部网关或经评审的分布式实现。无效标识符与容量耗尽均 fail closed。

`PubSoft.AspNetCore` HMAC 中间件默认保护全部路径；排除前缀为 opt-in。应用必须注册 `IAppSecretResolver`。body 哈希为 opt-in 且上限 16 MiB。默认 nonce store 为进程内，多实例部署应替换。安全响应头默认开启；HSTS 仅对 HTTPS 输出，除非开发期显式启用。

## 验证

```bash
dotnet test PubSoft.sln
bash scripts/verify-package-consumer.sh
```

安全测试覆盖 AEAD 篡改/密钥轮换/context 绑定、旧 CBC 读取-重加密、畸形签名/重放并发、限流并发/容量与 ASP.NET 中间件行为。这些测试不验证生产 secret manager 或分布式 nonce store 实现——需按部署做集成测试。
