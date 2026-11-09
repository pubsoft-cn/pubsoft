using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


/// <summary>Xiaomi account OAuth - Xiaomi OAuth provider.</summary>
public sealed class XiaomiOAuthProvider : GenericOAuthProvider
{
    /// <summary>Creates the provider.</summary>
    public XiaomiOAuthProvider(
IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<XiaomiOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Xiaomi;
    protected override string AuthorizeEndpoint => "https://account.xiaomi.com/oauth2/authorize";
    protected override string TokenEndpoint => "https://account.xiaomi.com/oauth2/token";
    protected override string UserInfoEndpoint => "https://open.account.xiaomi.com/user/profile";
    protected override string DefaultScope => "user/profile:read";

    protected override async Task<OAuthUserInfoResult> FetchUserInfoAsync(OAuthChannelConfig config, string accessToken, CancellationToken ct)
    {
        var url = $"{UserInfoEndpoint}?clientId={Uri.EscapeDataString(config.AppId)}&token={Uri.EscapeDataString(accessToken)}";
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
