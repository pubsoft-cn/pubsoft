using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


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
