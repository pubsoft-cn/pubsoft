using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


/// <summary>Google Sign-In OAuth - Google OAuth provider.</summary>
public sealed class GoogleOAuthProvider : GenericOAuthProvider
{
    /// <summary>Creates the provider.</summary>
    public GoogleOAuthProvider(
IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<GoogleOAuthProvider>? l = null) : base(cp, hc, l) { }
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
