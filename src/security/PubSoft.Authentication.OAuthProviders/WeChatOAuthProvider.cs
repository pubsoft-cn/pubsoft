using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

/// <summary>
/// WeChat OAuth provider — mini program (jscode2session), Official Account H5 (sns/oauth2),
/// Open Platform QR connect. All calls go through HttpClient with the host-configured resilience.
/// </summary>
public sealed class WeChatOAuthProvider : OAuthProviderBase
{
    private readonly IWeChatAccessTokenProvider _accessTokenProvider;

    public WeChatOAuthProvider(
        IOAuthChannelConfigProvider configProvider,
        IWeChatAccessTokenProvider accessTokenProvider,
        HttpClient httpClient,
        ILogger<WeChatOAuthProvider>? logger = null)
        : base(configProvider, httpClient, logger)
    {
        _accessTokenProvider = accessTokenProvider;
    }

    public override string Provider => OAuthProviderNames.WeChat;

    public override bool SupportsChannelType(string channelType) =>
        channelType is OAuthChannelTypes.MiniProgram
            or OAuthChannelTypes.OfficialAccount
            or OAuthChannelTypes.OpenPlatform
            or OAuthChannelTypes.Web;

    public override Task<OAuthAuthorizeUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default)
    {
        var scope = request.Scope ?? (request.ChannelType == OAuthChannelTypes.OpenPlatform ? "snsapi_login" : "snsapi_userinfo");
        var baseUrl = request.ChannelType == OAuthChannelTypes.OpenPlatform
            ? "https://open.weixin.qq.com/connect/qrconnect"
            : "https://open.weixin.qq.com/connect/oauth2/authorize";

        var state = request.State ?? Guid.NewGuid().ToString("N");
        var redirectUri = Uri.EscapeDataString(request.RedirectUri ?? "");
        var url = $"{baseUrl}?appid={request.AppId}&redirect_uri={redirectUri}&response_type=code&scope={scope}&state={state}#wechat_redirect";

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
        var isOAuth = channelType is OAuthChannelTypes.OfficialAccount
            or OAuthChannelTypes.OpenPlatform
            or OAuthChannelTypes.Web;

        var url = isOAuth
            ? $"https://api.weixin.qq.com/sns/oauth2/access_token?appid={config.AppId}&secret={config.AppSecret}&code={Uri.EscapeDataString(code)}&grant_type=authorization_code"
            : $"https://api.weixin.qq.com/sns/jscode2session?appid={config.AppId}&secret={config.AppSecret}&js_code={Uri.EscapeDataString(code)}&grant_type=authorization_code";

        var sw = Stopwatch.StartNew();
        var response = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        sw.Stop();
        Logger?.LogInformation("WeChat OAuth appId={AppId} channel={Channel} elapsed={Elapsed}ms", appId, channelType, sw.ElapsedMilliseconds);

        if (response.TryGetProperty("errcode", out var errCode) && errCode.GetInt32() != 0)
        {
            var msg = GetString(response, "errmsg") ?? "WeChat login failed";
            Logger?.LogWarning("WeChat OAuth failed: errcode={ErrCode} errmsg={Msg}", errCode.GetInt32(), msg);
            return OAuthCodeExchangeResult.Fail(msg);
        }

        return new OAuthCodeExchangeResult
        {
            Success = true,
            OpenId = GetString(response, "openid") ?? "",
            UnionId = GetString(response, "unionid"),
            SessionKey = GetString(response, "session_key"),
        };
    }

    public override async Task<OAuthUserInfoResult> GetUserInfoAsync(string appId, string accessToken, CancellationToken ct = default)
    {
        var url = $"https://api.weixin.qq.com/sns/userinfo?access_token={accessToken}";
        var response = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        if (response.TryGetProperty("errcode", out var errCode) && errCode.GetInt32() != 0)
            return OAuthUserInfoResult.Fail(GetString(response, "errmsg") ?? "Failed to get user info");

        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(response, "openid") ?? "",
            UnionId = GetString(response, "unionid"),
            NickName = GetString(response, "nickname"),
            AvatarUrl = GetString(response, "headimgurl"),
            Province = GetString(response, "province"),
            City = GetString(response, "city"),
            Gender = GetString(response, "sex"),
        };
    }

    public override async Task<OAuthPhoneResult> GetPhoneNumberAsync(string appId, string code, CancellationToken ct = default)
    {
        var tokenResult = await _accessTokenProvider.GetAsync(appId, ct).ConfigureAwait(false);
        if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.AccessToken))
            return OAuthPhoneResult.Fail(tokenResult.Message ?? "Failed to get access_token");

        var accessToken = tokenResult.AccessToken;

        // StringContent keeps a Content-Length — the WeChat WAF rejects chunked POSTs (HTTP 412).
        var phoneUrl = $"https://api.weixin.qq.com/wxa/business/getuserphonenumber?access_token={accessToken}";
        string json;
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("code", code);
                writer.WriteEndObject();
            }
            json = Encoding.UTF8.GetString(stream.ToArray());
        }
        var body = new StringContent(json, Encoding.UTF8, "application/json");
        var phoneJson = await PostJsonAsync(phoneUrl, body, ct).ConfigureAwait(false);
        if (phoneJson.TryGetProperty("errcode", out var phoneErr) && phoneErr.GetInt32() != 0)
            return OAuthPhoneResult.Fail(GetString(phoneJson, "errmsg") ?? "Failed to get phone number");

        var info = phoneJson.GetProperty("phone_info");
        return new OAuthPhoneResult
        {
            Success = true,
            PhoneNumber = GetString(info, "phoneNumber"),
            PurePhoneNumber = GetString(info, "purePhoneNumber"),
            CountryCode = GetString(info, "countryCode"),
        };
    }
}
