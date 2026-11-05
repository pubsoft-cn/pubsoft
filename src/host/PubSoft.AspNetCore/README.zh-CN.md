# PubSoft.AspNetCore

[English](README.md) | **简体中文**

`PubSoft.AspNetCore` 把框架无关的 PubSoft 包适配到 ASP.NET Core 的 opt-in 中间件与端点助手。没有任何东西自动启用。宿主仍负责认证/授权策略、可信代理配置、密钥存储与中间件顺序。

## 异常处理与安全响应头

尽早注册异常处理以包裹下游中间件与端点：

```csharp
using PubSoft.AspNetCore;

app.UsePubSoftExceptionHandling();
app.UsePubSoftSecurityHeaders(new SecurityHeadersOptions
{
    ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'",
    StrictTransportSecurity = "max-age=31536000; includeSubDomains"
});
```

异常中间件把 `DomainException` 与参数错误映射 400（`DomainException.StatusCode` 可声明其他 4xx，如冲突用 409——非 4xx 回退 400）、授权异常映射 401、意外错误在生产映射为通用 500。不改写调用方取消或响应头已发出后的异常。开发环境响应可含异常细节/堆栈——勿对外暴露开发模式。安全头值拒绝 CR/LF。HSTS 默认仅对 HTTPS 请求输出；TLS 在可信代理终止时须在这些中间件之前配置 forwarded headers，开发环境勿盲开 HSTS。

## 请求收尾与审计

scoped 保存协调器可在成功响应开始前执行宿主注册的回调：

```csharp
services.AddPubSoftRequestSaveChanges();
app.UsePubSoftRequestSaveChanges();
```

中间件发现每个实现 `IPubSoftSaveParticipant` 的 DI 注册并在成功响应前各保存一次；`PubSoftDbContext` 与 `PubSoftUnitOfWork<T>` 实现该契约，`AddPubSoftSaveParticipant<T>`/`AddPubSoftRequestUnitOfWork<T>` 注册自定义 participant。经 scoped `PubSoftRequestSaveChanges` 注册的显式回调仍会执行。保存顺序执行、至多一次、仅对 <400 响应；多次保存非原子事务——后面的失败可能发生在前面成功保存之后。

审计 finalize 同为 opt-in，需要宿主的 audit scope 与 sink 注册：

```csharp
app.UsePubSoftAudit(new AuditMiddlewareOptions
{
    CaptureClientIp = false,
    CaptureUserAgent = false,
    CaptureActor = false
});
```

IP/User-Agent/Actor 采集默认关闭——评审隐私、代理、身份与保留要求后再启用。actor 映射由宿主经 `IAuditActorAccessor` 提供；本适配器不解释 claims。审计 sink 失败记日志，不替换应用响应。

## HMAC 请求验证

注册 secret resolver 与 HMAC 验证，中间件置于受保护端点前：

```csharp
services.AddScoped<IAppSecretResolver, SecretManagerAppSecretResolver>();
services.AddPubSoftHmacValidation(
    options =>
    {
        options.RequireBodyHash = true;
        options.MaximumBodyBytes = 1_048_576;
        options.ExcludedPathPrefixes = ["/health"];
    },
    signature => signature.MaxTimestampSkewSeconds = 300);

app.UsePubSoftHmacValidation();
```

请求携带 `X-Fend-App-Id`、`X-Fend-Signature`、`X-Fend-Timestamp`、`X-Fend-Nonce` 各一。HMAC-SHA256 签名按换行覆盖 HTTP 方法、`Request.Path` 拼接原始 `Request.QueryString`（含前导 `?`，无 query 时为空）、时间戳、nonce 与可选的小写 SHA-256 body 哈希。客户端必须按实际发送的 query 原样入签——代理或客户端重排、重编码或改变分隔符都会改变签名串导致验签失败。中间件拒绝缺失/歧义 header、无效签名、过期时间戳与 nonce 重放。body 哈希会缓冲请求并强制 `MaximumBodyBytes`（默认 1 MiB、上限 16 MiB），随后为下游 rewind。

`AddPubSoftHmacValidation` 默认 `MemoryNonceStore`——只防单进程内重放；多实例生产部署需要经评审的原子共享 `INonceStore`；secret 解析须用批准的 secret manager。排除路径绕过签名验证——列表保持窄而显式。secret 解析不可用时中间件返回 503，不暴露密钥。

## Forwarded Headers

`RemoteIpAddress` 是全栈唯一的客户端 IP 权威来源，任何组件不得自行读原始 `X-Forwarded-For`。反代部署下，在所有 IP 消费方之前插一次 forwarded-header 处理：

```csharp
app.UsePubSoftForwardedHeaders(options =>
{
    // 默认：仅信平台默认（loopback peer）。
    // 后端直连可达时钉死代理 peer 集合：
    // options.KnownProxies.Add("10.0.0.1");
    // options.KnownNetworks.Add("172.16.0.0/12");
    // 只发 X-Real-IP 的代理（无 X-Forwarded-For）：
    // options.RealIp = true;
    // 仅当 socket 只能被代理触达（回环端口 / 私网段）：
    // options.TrustAnyProxy = true;
});
app.UsePubSoftRateLimiting();
```

直连可达的后端开 `TrustAnyProxy` = 任意客户端可伪造源 IP（限流绕过 + 审计投毒）。`RealIp = true` 在没有 `X-Forwarded-For` 且 socket peer 可信时采纳 `X-Real-IP`——默认关闭，且 `AddPubSoftForwardedHeaders` 会直接拒绝（平台 `ForwardedHeadersOptions` 表达不了）。`KnownProxies`/`KnownNetworks` 里格式非法的条目在启动期抛错而非静默丢弃。`AddPubSoftForwardedHeaders` 是服务侧等价件（注册 `ForwardedHeadersOptions`，供自行 `UseForwardedHeaders()` 的宿主用）。`ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders` 运行时检测该错配（私网 socket peer + 公网宣告客户端）：两个限流中间件都会输出一次性告警并点名缺失的调用。

## IP 限流与健康端点

限流适配器是有界、进程内固定窗口限流器，只按规范化 `HttpContext.Connection.RemoteIpAddress` 分键：

```csharp
services.AddPubSoftRateLimiting(options =>
{
    options.RequestsPerWindow = 100;
    options.WindowSize = TimeSpan.FromMinutes(1);
    options.MaxIdentifiers = 100_000;
});

app.UseForwardedHeaders(); // 配置可信代理/网络的 ForwardedHeadersOptions
app.UsePubSoftRateLimiting();
```

它不自行读 `X-Forwarded-For`——限流前须先为可信代理配置 forwarded-header 处理。缺 remote IP 与标识符容量耗尽均 fail closed。限流器非集群配额；此适配器目前拒绝 user-ID 模式。

按路由配额可注册一个或多个命名策略并换用策略中间件：

```csharp
services.AddPubSoftRateLimitPolicy(options =>
{
    options.Name = "login-failures";
    options.RequestsPerWindow = 10;
    options.WindowSize = TimeSpan.FromMinutes(5);
    options.CountFailuresOnly = true;
    options.AppliesTo = context => context.Request.Path.StartsWithSegments("/api/login");
    options.IdentifierSelector = context =>
        IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString());
});

app.UsePubSoftRateLimitPolicies();
```

多策略命中同一请求时叠加生效。默认标识符为规范化 transport remote IP；自定义 selector 是宿主代码，必须用可信身份或经代理处理的值——绝不用未校验的 forwarded/client header。仅计失败策略在端点前检查已有计数、在下游管线返回或抛出时记录响应——同时并发在飞的失败可能在计数提交前全部通过，这是滥用节流而非严格并发限制。两种中间件变体均为进程内；集群级强制用经评审的分布式配额 provider。路由类别与哪些 4xx/5xx、404 或认证失败计数由宿主定义——`IsFailureResponse` 接收 `(HttpContext, statusCode)`，谓词可排除例如"已带凭证请求的 401"。

429 拒绝体默认 `{"error":"rate_limit_exceeded"}`；有 API 错误信封约定的宿主可为两个中间件统一覆写：

```csharp
services.AddPubSoftRateLimitRejection(context =>
    context.Response.WriteAsJsonAsync(new { code = 429, message = "rate limit exceeded" }));
```

聚合健康检查：注册 `PubSoft.Monitoring` 检查与聚合器，再显式 `app.MapPubSoftHealthEndpoint()`——只返回总体状态（`Unhealthy` 503；`Healthy`/`Degraded` 200）。按需加端点授权与限流。

## 仅开发环境 Controller

`AddPubSoftDevelopmentOnlyControllerConvention(environment)` 为 opt-in MVC 约定。标 `[DevelopmentOnly]` 的 controller 在非 Development 环境被摘除路由。它不是授权替代品——不用于保护非 controller 端点或替代访问控制。

## MVC 权限授权

加 controllers 后显式注册 MVC filter：

```csharp
services.AddControllers().AddPubSoftPermissionAuthorization();
```

全局 filter 只执行 controller/action 上显式的 `[RequirePermission]`/`[RequireCustomPermission]`/`[RequireExtPermission]` 元数据；每条要求都须通过，未标注 action 不受影响。无解析 subject 返回 401；租户 context 不匹配或缺授权返回 403。宿主须注册可信 subject accessor、租户上下文、权限定义与 grant provider。`PermissionExemptAttribute`/`IAllowAnonymous` 优先于同端点要求元数据。除此之外不实现角色/管理员 bypass 或通用 `[AllowAnonymous]` 策略——组合策略前在宿主决定这些语义。

## 机器凭证

`[RequireClientCredential]` 标记接受服务间凭证的端点，经 `X-Client-Id`/`X-Client-Secret` header。宿主注册 `IClientCredentialVerifier` 解析并校验（凭证存储完全宿主定义）。成功时 filter 挂机器 principal（`client_id` + `caller_type` claim + verifier 提供的 claim）、把已验证 client ID 存入 `PubSoft.ClientCredential.ClientId`，verifier 返回租户时建立 ambient 租户 scope。缺失/无效凭证返回无差别 401；标注端点未注册 verifier 时 fail-closed 403。设 `RequireClientCredentialAttribute.AllowAuthenticatedUser` 可同时放行已认证 principal（默认仅机器凭证）。

```csharp
services.AddSingleton<IClientCredentialVerifier, MyClientVerifier>();
services.AddControllers().AddPubSoftClientCredentialAuthorization();  // MVC
// 或 minimal API：endpoint.AddPubSoftClientCredential()
```

header 名与 claim 类型经 `ClientCredentialOptions` 可配。租户 scope 包裹端点执行并在之后还原。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.AspNetCore`: `AuditMiddleware` `AuditMiddlewareExtensions` `AuditMiddlewareOptions` `DevelopmentOnlyAttribute` `DevelopmentOnlyControllerConvention` `DevelopmentOnlyControllerConventionExtensions` `ExceptionHandlingMiddleware` `ExceptionHandlingMiddlewareExtensions` `HealthEndpointRouteBuilderExtensions` `HmacValidationMiddleware` `HmacValidationOptions` `HmacValidationServiceCollectionExtensions` `IAppSecretResolver` `IpNormalizer` `PubSoftRateLimitPolicy` `PubSoftRateLimitPolicyExtensions` `PubSoftRateLimitPolicyMiddleware` `PubSoftRateLimitPolicyOptions` `PubSoftRateLimitingExtensions` `PubSoftRateLimitingMiddleware` `PubSoftRequestSaveChanges` `PubSoftRequestSaveChangesExtensions` `PubSoftRequestSaveChangesMiddleware` `SecurityHeadersMiddleware` `SecurityHeadersMiddlewareExtensions` `SecurityHeadersOptions`
- `PubSoft.AspNetCore.Authentication`: `ClientCredentialAuthorizationExtensions` `ClientCredentialEndpointFilter` `ClientCredentialMvcAuthorizationFilter` `ClientCredentialOptions` `ClientCredentialVerificationResult` `IClientCredentialVerifier` `PrincipalSnapshotServiceCollectionExtensions` `RequireClientCredentialAttribute`
- `PubSoft.AspNetCore.Authorization`: `PermissionEndpointFilter` `PermissionEndpointFilterExtensions` `PermissionMvcAuthorizationExtensions` `PermissionMvcAuthorizationFilter`
## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~ExceptionHandlingMiddlewareTests
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~RateLimitingMiddlewareTests
bash scripts/verify-package-consumer.sh
```
