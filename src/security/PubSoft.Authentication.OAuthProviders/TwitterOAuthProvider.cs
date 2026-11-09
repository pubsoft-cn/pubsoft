using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


/// <summary>Twitter/X OAuth 2.0 with PKCE (User Context). The caller must supply a PKCE <see cref="OAuthAuthorizeUrlRequest.CodeChallenge"/> (S256); this package does not retain the verifier. - Twitter OAuth provider.</summary>
public sealed class TwitterOAuthProvider : GenericOAuthProvider
{
    /// <summary>Creates the provider.</summary>
    public TwitterOAuthProvider(
IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<TwitterOAuthProvider>? l = null) : base(cp, hc, l) { }
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
