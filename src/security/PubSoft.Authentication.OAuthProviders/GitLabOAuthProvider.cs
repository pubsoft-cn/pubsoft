using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


/// <summary>GitLab.com / self-hosted GitLab OAuth - GitLab OAuth provider.</summary>
public sealed class GitLabOAuthProvider : GenericOAuthProvider
{
    /// <summary>Creates the provider.</summary>
    public GitLabOAuthProvider(
IOAuthChannelConfigProvider cp, HttpClient hc, ILogger<GitLabOAuthProvider>? l = null) : base(cp, hc, l) { }
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
