using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


/// <summary>Toutiao OAuth -- website-app QR login - Toutiao OAuth provider.</summary>
public sealed class ToutiaoOAuthProvider : GenericOAuthProvider
{
    /// <summary>Creates the provider.</summary>
    public ToutiaoOAuthProvider(
IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<ToutiaoOAuthProvider>? l = null) : base(cp, hc, l) { }
    public override string Provider => OAuthProviderNames.Toutiao;
    protected override string AuthorizeEndpoint => "https://open.snssdk.com/auth/authorize";
    protected override string TokenEndpoint => "https://open.snssdk.com/auth/access_token";
    protected override string UserInfoEndpoint => "https://open.snssdk.com/auth/userinfo";
    protected override string DefaultScope => "user_info";
}
