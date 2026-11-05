---
purpose: 宿主侧安全接入检查清单
audience: 集成方宿主团队与评审者
owner: PubSoft 维护组
last-reviewed: 2026-11-03
---

# 宿主安全接入检查清单

PubSoft 各包默认 fail-closed，但有若干安全边界刻意交由宿主负责。本清单覆盖每个宿主在生产上线前应运行的接入校验。标注"不变量（invariant）"的条目，宿主应以回归测试持续断言。

## 重放防护（ClientSigner / 客户端凭据）

- [ ] Nonce 存于**共享**存储（如 Redis），而非实例内存——跨副本重放必须被拒绝。不变量：同一个 `(nonce, timestamp, signature)` 对第二个实例重放时失败。
- [ ] Nonce 占用是原子的（`SET key 1 NX EX`），不是先查后写。
- [ ] Nonce TTL ≥ `ClientSigner.NonceTtl`，使过期签名永远无法在时间窗内被重放。
- [ ] `X-Timestamp` 是 Unix **毫秒**——若用秒断言会在静默中禁用新鲜度检查。

## Forwarded 头 / 限流

- [ ] 宿主仅部署在会剥离外部 `X-Forwarded-For` 的网关或负载均衡之后。启用限流时，PubSoft 会清空 `KnownProxies`/`KnownIPNetworks`——缺少可信边缘部署会让所有基于 IP 的键都可被客户端伪造。
- [ ] 不变量：请求未经过可信代理时，伪造的 `X-Forwarded-For` 不改变身份桶。

## 租户与成员范围

- [ ] `HttpContextTenantOptions.ReadHeader` 仅在网关会剥离客户端租户头时启用。不变量：直连请求携带 `X-Tenant-Id` 不能跨租户读取。
- [ ] `IsMemberScoped=true` 且无 member ID 时返回空结果集（fail-closed），需对真实 DbContext 验证。
- [ ] `IgnoreScopeFilters()` 仅在刻意跨租户的路径调用（如匿名 relay 查询），绝不出现在普通应用服务中。

## JWT / JWKS

- [ ] `Jwt:SecretKey` 为 ≥32 字节真实熵，按环境轮换；同一密钥只与应被信任的发放方共享。
- [ ] 配置 `AuthServer` 时，RS256 令牌仅对 `JwksUri` 键验证——伪造 `alg`/密钥的令牌必须 401。
- [ ] 不变量：禁用 JWKS 不影响 HS256 验证（无降级）。

## 配置 / 密钥

- [ ] DB-first 配置提供方缓存不超过其 TTL 且遵守 `IsEnabled`；禁用一行配置无须重启即生效。
- [ ] `SlowQueryInterceptor` 记录 SQL 截断到 500 字符——完整语句体与参数值绝不写入日志。

## Outbox / 消息

- [ ] Outbox 消费者处理器是幂等的——至少一次投递意味着同一消息可到达两次；不变量：重投递不产生重复副作用。
