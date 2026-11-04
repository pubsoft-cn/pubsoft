using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class GenericOAuthWaveTests
{
    private sealed class Cfg(OAuthChannelConfig c) : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string a, CancellationToken ct = default) => Task.FromResult<OAuthChannelConfig?>(c);
        public Task<OAuthChannelConfig?> GetDefaultAsync(string p, string ch, CancellationToken ct = default) => Task.FromResult<OAuthChannelConfig?>(c);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string p, CancellationToken ct = default) => Task.FromResult(new List<OAuthChannelConfig> { c });
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default) => Task.FromResult(new List<OAuthChannelConfig> { c });
    }

    private sealed class Http2(params string[] bodies) : HttpMessageHandler
    {
        private int _i;
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Urls.Add(r.RequestUri!.ToString());
            var b = bodies[Math.Min(_i++, bodies.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(b, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static GitHubOAuthProvider P(Http2 http) =>
        new(new Cfg(new OAuthChannelConfig { AppId = "gh1", AppSecret = "s" }), new HttpClient(http));

    [Fact]
    public async Task AuthorizeUrlContainsEndpoints()
    {
        var r = await P(new Http2("")).GetAuthorizeUrlAsync(new OAuthAuthorizeUrlRequest { AppId = "gh1", RedirectUri = "https://cb", State = "s" });
        Assert.True(r.Success);
        Assert.Contains("github.com/login/oauth/authorize", r.AuthorizeUrl);
        Assert.Contains("client_id=gh1", r.AuthorizeUrl);
        Assert.Contains("state=s", r.AuthorizeUrl);
    }

    [Fact]
    public async Task ExchangeCodePostsTokenEndpoint()
    {
        var http = new Http2("""{"access_token":"tok","token_type":"bearer","scope":"read:user"}""");
        var r = await P(http).ExchangeCodeAsync("gh1", "code1");
        Assert.True(r.Success);
        Assert.Contains("github.com/login/oauth/access_token", http.Urls[0]);
    }

    [Fact]
    public async Task GetUserInfoFetchesEndpoint()
    {
        var http = new Http2("""{"id":123,"login":"user1"}""");
        var r = await P(http).GetUserInfoAsync("gh1", "tok");
        Assert.True(r.Success);
        Assert.Contains("api.github.com/user", http.Urls[0]);
    }

    [Fact]
    public void SupportsWebChannel()
    {
        Assert.True(P(new Http2("")).SupportsChannelType("Web"));
    }
}
