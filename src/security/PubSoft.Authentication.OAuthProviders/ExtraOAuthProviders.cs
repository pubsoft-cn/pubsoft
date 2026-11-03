using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

// ── China-market providers ──

/// <summary>Douyin (TikTok China) OAuth — website-app QR login. AppId=client_key, AppSecret=client_secret.</summary>
public sealed class DouyinOAuthProvider : GenericOAuthProvider
{
    public DouyinOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<DouyinOAuthProvider>? l = null) : base(cp, hc, l) { }
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

/// <summary>Toutiao OAuth — website-app QR login.</summary>
public sealed class ToutiaoOAuthProvider : GenericOAuthProvider
{
    public ToutiaoOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<ToutiaoOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Toutiao;
    protected override string AuthorizeEndpoint => "https://open.snssdk.com/auth/authorize";
    protected override string TokenEndpoint => "https://open.snssdk.com/auth/access_token";
    protected override string UserInfoEndpoint => "https://open.snssdk.com/auth/userinfo";
    protected override string DefaultScope => "user_info";
}

/// <summary>Taobao OAuth — user info requires the TOP API (top.taobao.userinfo.get).</summary>
public sealed class TaobaoOAuthProvider : GenericOAuthProvider
{
    public TaobaoOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<TaobaoOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Taobao;
    protected override string AuthorizeEndpoint => "https://oauth.taobao.com/authorize";
    protected override string TokenEndpoint => "https://oauth.taobao.com/token";
    protected override string UserInfoEndpoint => "https://eco.taobao.com/router/rest";
    protected override string DefaultScope => "user_info";

    protected override Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
        => Task.FromResult(OAuthUserInfoResult.Fail("Taobao user info requires the TOP API (top.taobao.userinfo.get)"));
}

/// <summary>Huawei account OAuth (HMS).</summary>
public sealed class HuaweiOAuthProvider : GenericOAuthProvider
{
    public HuaweiOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<HuaweiOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Huawei;
    protected override string AuthorizeEndpoint => "https://oauth-login.cloud.huawei.com/oauth2/v2/authorize";
    protected override string TokenEndpoint => "https://oauth-login.cloud.huawei.com/oauth2/v2/token";
    protected override string UserInfoEndpoint => "https://api.vmall.com/rest.php?nsp_api=huawei.account.getUserInfo";
    protected override string DefaultScope => "https://www.huawei.com/auth/account/user.base";

    protected override async Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var url = $"{UserInfoEndpoint}&nsp_token={accessToken}";
        var response = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(response, "userID") ?? GetString(response, "openId") ?? "",
            NickName = GetString(response, "displayName") ?? GetString(response, "nickName"),
            AvatarUrl = GetString(response, "headPictureURL"),
        };
    }
}

/// <summary>Xiaomi account OAuth.</summary>
public sealed class XiaomiOAuthProvider : GenericOAuthProvider
{
    public XiaomiOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<XiaomiOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Xiaomi;
    protected override string AuthorizeEndpoint => "https://account.xiaomi.com/oauth2/authorize";
    protected override string TokenEndpoint => "https://account.xiaomi.com/oauth2/token";
    protected override string UserInfoEndpoint => "https://open.account.xiaomi.com/user/profile";
    protected override string DefaultScope => "user/profile:read";

    protected override async Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var url = $"{UserInfoEndpoint}?clientId={{appId}}&token={accessToken}";
        var response = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(response, "unionId") ?? GetString(response, "userId") ?? "",
            NickName = GetString(response, "miliaoNick"),
            AvatarUrl = GetString(response, "miliaoIcon"),
        };
    }
}

// ── International providers ──

/// <summary>Microsoft Account / Microsoft Entra ID OAuth.</summary>
public sealed class MicrosoftOAuthProvider : GenericOAuthProvider
{
    public MicrosoftOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<MicrosoftOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Microsoft;
    protected override string AuthorizeEndpoint => "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
    protected override string TokenEndpoint => "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    protected override string UserInfoEndpoint => "https://graph.microsoft.com/v1.0/me";
    protected override string DefaultScope => "openid profile User.Read";

    protected override async Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await SendJsonAsync(req, ct).ConfigureAwait(false);
        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(response, "id") ?? "",
            NickName = GetString(response, "displayName") ?? GetString(response, "givenName"),
            Email = GetString(response, "mail") ?? GetString(response, "userPrincipalName"),
        };
    }
}

/// <summary>Google Sign-In OAuth.</summary>
public sealed class GoogleOAuthProvider : GenericOAuthProvider
{
    public GoogleOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<GoogleOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Google;
    protected override string AuthorizeEndpoint => "https://accounts.google.com/o/oauth2/auth";
    protected override string TokenEndpoint => "https://oauth2.googleapis.com/token";
    protected override string UserInfoEndpoint => "https://www.googleapis.com/oauth2/v2/userinfo";
    protected override string DefaultScope => "openid profile email";

    protected override async Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await SendJsonAsync(req, ct).ConfigureAwait(false);
        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(response, "id") ?? "",
            NickName = GetString(response, "name"),
            AvatarUrl = GetString(response, "picture"),
            Email = GetString(response, "email"),
        };
    }
}

/// <summary>GitLab.com / self-hosted GitLab OAuth.</summary>
public sealed class GitLabOAuthProvider : GenericOAuthProvider
{
    public GitLabOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<GitLabOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.GitLab;
    protected override string AuthorizeEndpoint => "https://gitlab.com/oauth/authorize";
    protected override string TokenEndpoint => "https://gitlab.com/oauth/token";
    protected override string UserInfoEndpoint => "https://gitlab.com/api/v4/user";
    protected override string DefaultScope => "read_user";

    protected override async Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await SendJsonAsync(req, ct).ConfigureAwait(false);
        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(response, "id") ?? "",
            NickName = GetString(response, "name") ?? GetString(response, "username"),
            AvatarUrl = GetString(response, "avatar_url"),
            Email = GetString(response, "email"),
        };
    }
}

/// <summary>
/// Twitter/X OAuth 2.0 with PKCE (User Context). The caller must supply a PKCE
/// <see cref="OAuthAuthorizeUrlRequest.CodeChallenge"/> (S256); this package does not retain the verifier.
/// </summary>
public sealed class TwitterOAuthProvider : GenericOAuthProvider
{
    public TwitterOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<TwitterOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Twitter;
    protected override string AuthorizeEndpoint => "https://twitter.com/i/oauth2/authorize";
    protected override string TokenEndpoint => "https://api.twitter.com/2/oauth2/token";
    protected override string UserInfoEndpoint => "https://api.twitter.com/2/users/me";
    protected override string DefaultScope => "users.read tweet.read";

    public override Task<OAuthAuthorizeUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CodeChallenge))
            return Task.FromResult(OAuthAuthorizeUrlResult.Fail("Twitter OAuth requires a PKCE code_challenge (OAuthAuthorizeUrlRequest.CodeChallenge)"));

        var state = request.State ?? Guid.NewGuid().ToString("N");
        var redirectUri = Uri.EscapeDataString(request.RedirectUri ?? "");
        var scope = Uri.EscapeDataString(request.Scope ?? DefaultScope);
        var url = $"{AuthorizeEndpoint}?response_type=code&client_id={Uri.EscapeDataString(request.AppId)}&redirect_uri={redirectUri}&scope={scope}&state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(request.CodeChallenge)}&code_challenge_method=S256";

        return Task.FromResult(new OAuthAuthorizeUrlResult
        {
            Success = true,
            AuthorizeUrl = url,
            State = state,
            AppId = request.AppId,
        });
    }

    protected override async Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var url = $"{UserInfoEndpoint}?user.fields=id,name,username,profile_image_url";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await SendJsonAsync(req, ct).ConfigureAwait(false);
        var data = response.TryGetProperty("data", out var dataEl) ? dataEl : response;
        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(data, "id") ?? "",
            NickName = GetString(data, "name") ?? GetString(data, "username"),
            AvatarUrl = GetString(data, "profile_image_url"),
        };
    }
}
