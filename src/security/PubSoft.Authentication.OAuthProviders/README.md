# PubSoft.Authentication.OAuthProviders

**English** | [简体中文](README.zh-CN.md)

OAuth channel provider adapters (namespace `PubSoft.OAuth`) — a uniform contract over 19 vendor login flows.

## Providers

| Region | Providers |
|---|---|
| China | WeChat (H5/mini-program/open-platform, `getuserphonenumber`), Alipay (RSA2 gateway sign + verify), WeCom, DingTalk (HmacSHA256), QQ, Gitee, Baidu, Weibo, Douyin, Toutiao, Taobao, Huawei, Xiaomi |
| Global | Apple, Microsoft, Google, GitHub, GitLab, Twitter |

## Contract

```csharp
public interface IOAuthProvider
{
    Task<OAuthUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default);
    Task<OAuthTokenResult> ExchangeCodeAsync(OAuthCodeExchangeRequest request, CancellationToken ct = default);
    Task<OAuthUserInfoResult> GetUserInfoAsync(OAuthUserInfoRequest request, CancellationToken ct = default);
    Task<OAuthPhoneNumberResult> GetPhoneNumberAsync(OAuthPhoneNumberRequest request, CancellationToken ct = default);
}
```

`IOAuthProviderRegistry.Resolve(provider, channelType, appId)` selects the provider instance for a channel account.

## Registration

```csharp
services.AddPubSoftOAuthProviders<MyChannelConfigProvider>();
```

- `IOAuthChannelConfigProvider` is host-implemented — channel credentials (`OAuthChannelConfig`: AppId/AppSecret/RedirectUri/`ExtraJson`) come from your store; the library never reads secrets from its own configuration.
- Every provider gets a typed `HttpClient` under `PubSoft.Resilience.Http` policy (total/attempt timeouts, retry, circuit breaker).
- Alipay channels use RSA2 keys in `ExtraJson` rather than AppSecret — the provider validates accordingly; a malformed `sign` value fails closed.
- Twitter `code_challenge` is an explicit parameter (`OAuthAuthorizeUrlRequest.CodeChallenge`).

## Boundaries

Providers return raw vendor identity data — user creation, account binding, session/JWT issuance, relay-state machines, and login endpoints are host orchestration. Secrets never enter logs or exception messages. Native-AOT compatible.
Authorization URLs encode all query values (`app_id`, `state`, `scope`, `redirect_uri`) per parameter. Provider response bodies are capped at 1 MiB of actual bytes — multi-byte UTF-8 payloads cannot bypass the limit.

