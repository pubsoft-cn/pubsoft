# PubSoft.Authentication.OAuthProviders

[English](README.md) | **简体中文**

OAuth 渠道 provider 适配器（命名空间 `PubSoft.OAuth`）——各厂商登录流程的统一契约。

## Provider 一览

| 区域 | Provider |
|---|---|
| 国内 | 微信（H5/小程序/开放平台扫码、`getuserphonenumber`）、支付宝（RSA2 网关签名+验签）、企业微信、钉钉（HmacSHA256）、QQ、Gitee、百度、微博、抖音、今日头条、淘宝、华为、小米 |
| 国际 | Apple、Microsoft、Google、GitHub、GitLab、Twitter |

## 契约

```csharp
public interface IOAuthProvider
{
    Task<OAuthUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default);
    Task<OAuthTokenResult> ExchangeCodeAsync(OAuthCodeExchangeRequest request, CancellationToken ct = default);
    Task<OAuthUserInfoResult> GetUserInfoAsync(OAuthUserInfoRequest request, CancellationToken ct = default);
    Task<OAuthPhoneNumberResult> GetPhoneNumberAsync(OAuthPhoneNumberRequest request, CancellationToken ct = default);
}
```

`IOAuthProviderRegistry.Resolve(provider, channelType, appId)` 为渠道账号选择 provider 实例。

## 注册

```csharp
services.AddPubSoftOAuthProviders<MyChannelConfigProvider>();
```

- `IOAuthChannelConfigProvider` 由宿主实现——渠道凭证（`OAuthChannelConfig`：AppId/AppSecret/RedirectUri/`ExtraJson`）来自你的存储，库不自行读配置中的密钥
- 每个 provider 获得 `PubSoft.Resilience.Http` 策略下的 typed `HttpClient`（总/单次超时、重试、熔断）
- 支付宝渠道用 `ExtraJson` 中的 RSA2 密钥而非 AppSecret——provider 据此校验；畸形 `sign` 值 fail closed
- Twitter `code_challenge` 为显式参数（`OAuthAuthorizeUrlRequest.CodeChallenge`）

## 边界

provider 只返回原始厂商身份数据——用户创建、账号绑定、会话/JWT 签发、relay state 机与登录端点均为宿主编排。密钥不进日志与异常消息。兼容 Native AOT。
授权 URL 对所有查询值（`app_id`/`state`/`scope`/`redirect_uri`）逐项编码。Provider 响应体按实际字节数限制 1 MiB——多字节 UTF-8 载荷无法绕过限制。

