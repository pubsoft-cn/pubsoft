using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


/// <summary>Taobao OAuth -- user info requires the TOP API (top.taobao.userinfo.get) - Taobao OAuth provider.</summary>
public sealed class TaobaoOAuthProvider : GenericOAuthProvider
{
    /// <summary>Creates the provider.</summary>
    public TaobaoOAuthProvider(
IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<TaobaoOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Taobao;
    protected override string AuthorizeEndpoint => "https://oauth.taobao.com/authorize";
    protected override string TokenEndpoint => "https://oauth.taobao.com/token";
    protected override string UserInfoEndpoint => "https://eco.taobao.com/router/rest";
    protected override string DefaultScope => "user_info";

    protected override Task<OAuthUserInfoResult> FetchUserInfoAsync(string accessToken, CancellationToken ct)
        => Task.FromResult(OAuthUserInfoResult.Fail("Taobao user info requires the TOP API (top.taobao.userinfo.get)"));
}
