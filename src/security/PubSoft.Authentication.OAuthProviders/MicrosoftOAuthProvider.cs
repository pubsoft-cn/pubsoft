using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


// ── International providers ──

/// <summary>Microsoft Account / Microsoft Entra ID OAuth provider.</summary>
public sealed class MicrosoftOAuthProvider : GenericOAuthProvider
{
    /// <summary>Creates the provider.</summary>
    public MicrosoftOAuthProvider(
IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<MicrosoftOAuthProvider>? l = null) : base(cp, hc, l) { }
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
