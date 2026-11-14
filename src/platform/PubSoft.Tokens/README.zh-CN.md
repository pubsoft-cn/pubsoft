---
purpose: PubSoft.Tokens 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Tokens

[English](README.md) | **简体中文**

`PubSoft.Tokens` 经 `HttpClient` 获取服务 token、经 `ICache` 缓存，并可注入到具名 API client。不决定调用方身份到服务凭证的映射，不判定 API 操作是否可安全重放。

## 注册

注册 `ICache` 实现与一个或多个具名凭证。示例使用进程内缓存，适合开发或单进程；生产宿主应注册经批准的缓存并从 secret manager 加载端点凭证，而非源码管控的配置。

```csharp
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Caching;
using PubSoft.Tokens;

services.AddPubSoftMemoryCache();
services.AddPubSoftTokens(options =>
{
    options.Credentials["billing-api-v1"] = new TokenCredential
    {
        Url = tokenEndpoint,
        Method = "POST",
        Body = JsonSerializer.Serialize(new
        {
            grant_type = "client_credentials",
            client_id = clientId,
            client_secret = clientSecret
        }),
        TokenPath = "access_token",
        ExpiryPath = "expires_in",
        Expiry = TimeSpan.FromMinutes(10),
        RefreshBefore = TimeSpan.FromMinutes(1),
        Timeout = TimeSpan.FromSeconds(5)
    };
});

services.AddHttpClient("billing-api")
    .AddTokenHandler("billing-api-v1");
```

POST body 以 JSON 发送。`TokenPath`/`ExpiryPath` 为点分 JSON 路径；动态过期按秒读取且不超出配置的 `Expiry`。token 端点调用有独立 `Timeout`。凭据发往 `Url`，故非 loopback 的 `http` 端点在 options 校验期即被拒绝；loopback 为本地开发豁免，确需例外的端点可逐凭据 `AllowInsecureUrl` 显式放行。凭证与 token 响应不进日志，URL user-info/query 不放密钥。provider 只记录固定失败事件/状态码，不附 transport/JSON 异常——异常细节可能含端点 URI 或响应派生数据。

需要调用方自身认证的 token 端点（client assertion、注册表特定头）由 `TokenCredential.ConfigureRequest` 覆盖——可选钩子，在 `Headers` 应用后、发送前执行。该钩子需编程设置（配置绑定无法生成委托）；`private_key_jwt` 形态认证在钩子内用 `PubSoft.Authentication.Jwt` 的 `ClientAssertion.Sign` 签发。

宿主须先注册 `ICache` 再解析 token provider。缓存键由 credential key 派生，与 URL 或 client 身份无关。每个应用/缓存命名空间用不同的 credential key；更换某 key 代表的身份时，版本化该 key 或移除旧缓存 token。内置刷新 single-flight 为进程内——多实例可能并发调 token 端点；共享缓存不引入分布式锁。

已选凭证但取不到非空 token 时，`TokenDelegatingHandler` 抛 `HttpRequestException`、不发出受保护请求；完全未选凭证时原样放行——依赖认证才能工作的端点不要依赖该行为。

## 逐请求覆盖

凭证与 header 设置可在单个请求上覆盖：

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "/invoices")
    .SetTokenCredentialKey("billing-api-v1")
    .SetTokenHeaderName("Authorization")
    .SetTokenHeaderPrefix("Bearer ");
```

未显式 opt-in 时 401 响应直接返回，不做 token 刷新/重放。确认端点可安全重复的操作上可 opt-in：

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "/invoices")
    .EnableTokenUnauthorizedRetry();
```

handler 使缓存 token 失效并至多重试一次。opt-in 时把请求体缓冲进内存以支持重放——仅用于应用语义幂等的操作，可重放请求体保持有界。刷新失败则返回原 401，不发第二次请求。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Tokens`: `HttpTokenProvider` `ITokenProvider` `TokenCredential` `TokenDelegatingHandler` `TokenHandlerOptions` `TokenManagerOptions` `TokenRequestOptions` `TokenServiceCollectionExtensions`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~TokenTests
bash scripts/verify-package-consumer.sh
```

测试覆盖 token 缓存、动态过期、单进程并发刷新、端点超时/取消、缺 token fail-closed、端点异常日志脱敏与请求级 401 opt-in。不验证生产 secret manager、共享缓存隔离、分布式刷新协调或端点幂等。

## 自签机器令牌

`MachineTokenProvider` 本地签发 HS256 access token（走 `PubSoft.Authentication.Jwt`），按 credential key 缓存到 `Lifetime - RefreshBefore`——无外部 STS 场景的标准机到机凭证。配合 `TokenDelegatingHandler` 为出站 `HttpClient` 注入 Bearer。

```csharp
// 绑定 Jwt:Issuer / Jwt:Audience / Jwt:SecretKey（SigningKey 别名同样接受），
// 以及可选 KeyId / Lifetime / RefreshBefore / Credentials；启动期校验。
services.AddPubSoftMachineTokens(configuration);
```

issuer/audience 与验证面 `ValidIssuer`/`ValidAudience` 按序数比较（区分大小写）——签发方
与验证方必须读同一份配置值，任一侧都不得硬编码字面量。签名密钥需含 256–1024 bit 密钥材料；
生产密钥走 secret manager，不进源码管控配置。

不走配置绑定、显式给 options 时：

```csharp
services.AddPubSoftMachineTokens(o =>
{
    o.Issuer = "orders"; o.Audience = "inventory";
    o.SigningKey = configuration["Jwt:SecretKey"]!;
    o.Credentials["inventory"] = new MachineTokenCredential { Claims = { ["caller_type"] = "Machine" } };
});
```
HTTP 提供方与本机签发共存时，经 keyed DI 确定性解析本机签发：`[FromKeyedServices(MachineTokenProvider.ServiceKey)]`（`"machine"`）。

