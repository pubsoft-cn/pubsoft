using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

// ── China-market providers ──

/// <summary>Douyin (TikTok China) OAuth -- website-app QR login. AppId=client_key, AppSecret=client_secret - Douyin OAuth provider.</summary>
public sealed class DouyinOAuthProvider : GenericOAuthProvider
{
    /// <summary>Creates the provider.</summary>
    public DouyinOAuthProvider(
IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<DouyinOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Douyin;
    protected override string AuthorizeEndpoint => "https://open.douyin.com/platform/oauth/connect";
    protected override string TokenEndpoint => "https://open.douyin.com/oauth/access_token/";
    protected override string UserInfoEndpoint => "https://open.douyin.com/oauth/userinfo/";
    protected override string DefaultScope => "user_info";

    protected override async Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var url = $"{UserInfoEndpoint}?access_token={accessToken}";
        var response = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(response, "open_id") ?? "",
            NickName = GetString(response, "nickname"),
            AvatarUrl = GetString(response, "avatar"),
            Province = GetString(response, "province"),
            City = GetString(response, "city"),
            Gender = GetString(response, "gender"),
        };
    }
}
