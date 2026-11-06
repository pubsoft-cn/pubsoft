using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

/// <summary>
/// WeCom (Enterprise WeChat) OAuth provider — web-auth code → userid/openid.
/// Flow: code → access_token (corpid+corpsecret) → getuserinfo (code).
/// AppId=corpid, AppSecret=corpsecret.
/// </summary>
public sealed class WeComOAuthProvider : OAuthProviderBase
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private static readonly ConcurrentDictionary<string, (string token, DateTime expiresAt)> _tokenCache = new(StringComparer.Ordinal);

    public WeComOAuthProvider(
        IOAuthChannelConfigProvider configProvider,
        HttpClient httpClient,
        ILogger<WeComOAuthProvider>? logger = null)
        : base(configProvider, httpClient, logger) { }

    public override string Provider => OAuthProviderNames.WeCom;

    public override bool SupportsChannelType(string channelType) =>
        channelType is OAuthChannelTypes.Web or OAuthChannelTypes.OpenPlatform;

    public override Task<OAuthAuthorizeUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default)
    {
        var state = request.State ?? Guid.NewGuid().ToString("N");
        var redirectUri = Uri.EscapeDataString(request.RedirectUri ?? "");
        var url = $"https://open.weixin.qq.com/connect/oauth2/authorize?appid={Uri.EscapeDataString(request.AppId)}&redirect_uri={redirectUri}&response_type=code&scope=snsapi_base&state={Uri.EscapeDataString(state)}#wechat_redirect";

        return Task.FromResult(new OAuthAuthorizeUrlResult
        {
            Success = true,
            AuthorizeUrl = url,
            State = state,
            AppId = request.AppId,
        });
    }

    public override async Task<OAuthCodeExchangeResult> ExchangeCodeAsync(string appId, string code, string? channelType = null, CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(appId, ct).ConfigureAwait(false);
        var accessToken = await GetAccessTokenAsync(config.AppId, config.AppSecret!, ct).ConfigureAwait(false);

        var url = $"https://qyapi.weixin.qq.com/cgi-bin/auth/getuserinfo?access_token={accessToken}&code={Uri.EscapeDataString(code)}";
        var sw = Stopwatch.StartNew();
        var response = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        sw.Stop();
        Logger?.LogInformation("WeCom getuserinfo corpId={CorpId} elapsed={Elapsed}ms", appId, sw.ElapsedMilliseconds);

        if (response.TryGetProperty("errcode", out var errCode) && errCode.GetInt32() != 0)
            return OAuthCodeExchangeResult.Fail(GetString(response, "errmsg") ?? "WeCom login failed");

        var userId = GetString(response, "userid");
        var openId = GetString(response, "openid");
        var resolved = !string.IsNullOrWhiteSpace(userId) ? userId : openId;
        if (string.IsNullOrWhiteSpace(resolved))
            return OAuthCodeExchangeResult.Fail("WeCom login failed: no user identifier returned");

        return new OAuthCodeExchangeResult
        {
            Success = true,
            OpenId = openId ?? "",
            UserId = userId,
        };
    }

    public override Task<OAuthUserInfoResult> GetUserInfoAsync(string appId, string accessToken, CancellationToken ct = default)
    {
        return Task.FromResult(OAuthUserInfoResult.Fail("WeCom user info is returned by ExchangeCodeAsync"));
    }

    private async Task<string> GetAccessTokenAsync(string corpId, string corpSecret, CancellationToken ct)
    {
        if (_tokenCache.TryGetValue(corpId, out var cached) && cached.expiresAt > _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5))
            return cached.token;

        var url = $"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={corpId}&corpsecret={Uri.EscapeDataString(corpSecret)}";
        var response = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        if (response.TryGetProperty("errcode", out var errCode) && errCode.GetInt32() != 0)
            throw new InvalidOperationException($"WeCom failed to get access_token: {GetString(response, "errmsg")}");

        var token = GetString(response, "access_token") ?? "";
        var expiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(GetInt(response, "expires_in", 7200));

        _tokenCache[corpId] = (token, expiresAt);
        return token;
    }
}
