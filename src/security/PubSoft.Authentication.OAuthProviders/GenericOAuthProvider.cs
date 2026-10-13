using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

/// <summary>
/// Generic OAuth 2.0 provider for standard code flows (QQ/GitHub/Gitee/Baidu/Weibo/Apple, ...).
/// Derived classes only supply the provider name, endpoint URLs, and claim mapping.
/// </summary>
public abstract class GenericOAuthProvider : OAuthProviderBase
{
    protected abstract string AuthorizeEndpoint { get; }
    protected abstract string TokenEndpoint { get; }
    protected abstract string UserInfoEndpoint { get; }
    protected virtual string DefaultScope => "openid profile";

    protected GenericOAuthProvider(
        IOAuthChannelConfigProvider configProvider,
        HttpClient httpClient,
        ILogger? logger = null)
        : base(configProvider, httpClient, logger) { }

    public override bool SupportsChannelType(string channelType) =>
        channelType is OAuthChannelTypes.Web or OAuthChannelTypes.OpenPlatform;

    public override Task<OAuthAuthorizeUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default)
    {
        var state = request.State ?? Guid.NewGuid().ToString("N");
        var redirectUri = Uri.EscapeDataString(request.RedirectUri ?? "");
        var scope = request.Scope ?? DefaultScope;
        var url = $"{AuthorizeEndpoint}?client_id={request.AppId}&redirect_uri={redirectUri}&response_type=code&scope={Uri.EscapeDataString(scope)}&state={state}";

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
        var redirectUri = config.RedirectUri ?? "";

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = config.AppId,
            ["client_secret"] = config.AppSecret!,
        };
        if (!string.IsNullOrWhiteSpace(redirectUri))
            formData["redirect_uri"] = redirectUri;

        var content = new FormUrlEncodedContent(formData);
        var root = await PostJsonAsync(TokenEndpoint, content, ct).ConfigureAwait(false);

        if (root.TryGetProperty("error", out var errEl))
        {
            var msg = errEl.GetString() ?? "OAuth token exchange failed";
            if (root.TryGetProperty("error_description", out var desc))
                msg += $": {desc.GetString()}";
            return OAuthCodeExchangeResult.Fail(msg);
        }

        var accessToken = GetString(root, "access_token") ?? "";
        var refreshToken = GetString(root, "refresh_token");
        var openId = GetString(root, "openid");

        // When the token response carries no openid, fall back to userinfo.
        if (string.IsNullOrWhiteSpace(openId) && !string.IsNullOrWhiteSpace(accessToken))
        {
            var userInfo = await FetchUserInfoAsync(accessToken, ct).ConfigureAwait(false);
            if (userInfo.Success)
                openId = userInfo.OpenId;
        }

        return new OAuthCodeExchangeResult
        {
            Success = !string.IsNullOrWhiteSpace(accessToken),
            OpenId = openId ?? "",
            AccessToken = accessToken,
            RefreshToken = refreshToken,
        };
    }

    public override Task<OAuthUserInfoResult> GetUserInfoAsync(string appId, string accessToken, CancellationToken ct = default)
        => FetchUserInfoAsync(accessToken, ct);

    /// <summary>Derived classes may override to map provider-specific user-info fields.</summary>
    protected virtual async Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var url = $"{UserInfoEndpoint}?access_token={accessToken}";
        var response = await FetchJsonAsync(url, ct).ConfigureAwait(false);

        return new OAuthUserInfoResult
        {
            Success = true,
            OpenId = GetString(response, "openid") ?? GetString(response, "id") ?? "",
            NickName = GetString(response, "nickname") ?? GetString(response, "name") ?? GetString(response, "login"),
            AvatarUrl = GetString(response, "avatar_url") ?? GetString(response, "avatar") ?? GetString(response, "picture"),
            Email = GetString(response, "email"),
        };
    }
}

// ── Standard generic providers ──

public sealed class QQOAuthProvider : GenericOAuthProvider
{
    public QQOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<QQOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.QQ;
    protected override string AuthorizeEndpoint => "https://graph.qq.com/oauth2.0/authorize";
    protected override string TokenEndpoint => "https://graph.qq.com/oauth2.0/token";
    protected override string UserInfoEndpoint => "https://graph.qq.com/user/get_user_info";
    protected override string DefaultScope => "get_user_info";
}

public sealed class GitHubOAuthProvider : GenericOAuthProvider
{
    public GitHubOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<GitHubOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.GitHub;
    protected override string AuthorizeEndpoint => "https://github.com/login/oauth/authorize";
    protected override string TokenEndpoint => "https://github.com/login/oauth/access_token";
    protected override string UserInfoEndpoint => "https://api.github.com/user";
    protected override string DefaultScope => "read:user user:email";
}

public sealed class GiteeOAuthProvider : GenericOAuthProvider
{
    public GiteeOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<GiteeOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Gitee;
    protected override string AuthorizeEndpoint => "https://gitee.com/oauth/authorize";
    protected override string TokenEndpoint => "https://gitee.com/oauth/token";
    protected override string UserInfoEndpoint => "https://gitee.com/api/v5/user";
    protected override string DefaultScope => "user_info";
}

public sealed class BaiduOAuthProvider : GenericOAuthProvider
{
    public BaiduOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<BaiduOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Baidu;
    protected override string AuthorizeEndpoint => "https://openapi.baidu.com/oauth/2.0/authorize";
    protected override string TokenEndpoint => "https://openapi.baidu.com/oauth/2.0/token";
    protected override string UserInfoEndpoint => "https://openapi.baidu.com/rest/2.0/passport/users/getInfo";
    protected override string DefaultScope => "basic";
}

public sealed class WeiboOAuthProvider : GenericOAuthProvider
{
    public WeiboOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<WeiboOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Weibo;
    protected override string AuthorizeEndpoint => "https://api.weibo.com/oauth2/authorize";
    protected override string TokenEndpoint => "https://api.weibo.com/oauth2/access_token";
    protected override string UserInfoEndpoint => "https://api.weibo.com/2/users/show";
    protected override string DefaultScope => "all";
}

public sealed class AppleOAuthProvider : GenericOAuthProvider
{
    public AppleOAuthProvider(IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<AppleOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Apple;
    protected override string AuthorizeEndpoint => "https://appleid.apple.com/auth/authorize";
    protected override string TokenEndpoint => "https://appleid.apple.com/auth/token";
    protected override string UserInfoEndpoint => "https://appleid.apple.com/auth/userinfo";
    protected override string DefaultScope => "name email";
}
