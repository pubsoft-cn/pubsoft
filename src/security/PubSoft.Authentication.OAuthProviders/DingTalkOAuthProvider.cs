using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

/// <summary>
/// DingTalk OAuth provider — QR login / H5 code → openid/unionid.
/// API: POST https://oapi.dingtalk.com/sns/getuserinfo_bycode
/// Signing: HmacSHA256(timestamp + "\n" + appSecret) → Base64 → UrlEncode
/// AppId=appKey, AppSecret=appSecret.
/// </summary>
public sealed class DingTalkOAuthProvider : OAuthProviderBase
{
    public DingTalkOAuthProvider(
        IOAuthChannelConfigProvider configProvider,
        HttpClient httpClient,
        ILogger<DingTalkOAuthProvider>? logger = null)
        : base(configProvider, httpClient, logger) { }

    public override string Provider => OAuthProviderNames.DingTalk;

    public override bool SupportsChannelType(string channelType) =>
        channelType is OAuthChannelTypes.OpenPlatform or OAuthChannelTypes.Web;

    public override Task<OAuthAuthorizeUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default)
    {
        var state = request.State ?? Guid.NewGuid().ToString("N");
        var redirectUri = Uri.EscapeDataString(request.RedirectUri ?? "");
        var url = $"https://login.dingtalk.com/oauth2/auth?redirect_uri={redirectUri}&response_type=code&client_id={request.AppId}&scope=openid&state={state}&prompt=consent";

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
        var appKey = config.AppId;
        var appSecret = config.AppSecret!;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signContent = timestamp + "\n" + appSecret;
        var signature = Convert.ToBase64String(new HMACSHA256(Encoding.UTF8.GetBytes(appSecret)).ComputeHash(Encoding.UTF8.GetBytes(signContent)));

        var url = $"https://oapi.dingtalk.com/sns/getuserinfo_bycode?accessKey={Uri.EscapeDataString(appKey)}&timestamp={timestamp}&signature={Uri.EscapeDataString(signature)}";
        // StringContent keeps a Content-Length — same WAF consideration as WeChat.
        string json;
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("tmp_auth_code", code);
                writer.WriteEndObject();
            }
            json = Encoding.UTF8.GetString(stream.ToArray());
        }
        var body = new StringContent(json, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        var response = await PostJsonAsync(url, body, ct).ConfigureAwait(false);
        sw.Stop();
        Logger?.LogInformation("DingTalk getuserinfo_bycode appKey={AppKey} elapsed={Elapsed}ms", appId, sw.ElapsedMilliseconds);

        if (response.TryGetProperty("errcode", out var errCode) && errCode.GetInt32() != 0)
            return OAuthCodeExchangeResult.Fail(GetString(response, "errmsg") ?? "DingTalk login failed");

        if (response.TryGetProperty("user_info", out var userInfo))
        {
            var openId = GetString(userInfo, "openid");
            var unionId = GetString(userInfo, "unionid");
            var nick = GetString(userInfo, "nick");
            var resolved = !string.IsNullOrWhiteSpace(openId) ? openId : unionId;
            if (string.IsNullOrWhiteSpace(resolved))
                return OAuthCodeExchangeResult.Fail("DingTalk login failed: no user identifier returned");

            return new OAuthCodeExchangeResult
            {
                Success = true,
                OpenId = openId ?? "",
                UnionId = unionId,
                NickName = nick,
            };
        }

        return OAuthCodeExchangeResult.Fail("Malformed DingTalk response");
    }

    public override Task<OAuthUserInfoResult> GetUserInfoAsync(string appId, string accessToken, CancellationToken ct = default)
    {
        return Task.FromResult(OAuthUserInfoResult.Fail("DingTalk user info is returned by ExchangeCodeAsync"));
    }
}
