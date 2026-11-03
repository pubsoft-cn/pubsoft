using System.Net;
using System.Text;
using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OAuthExchangeWaveTests
{
    private sealed class Cfg(OAuthChannelConfig config) : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string appId, CancellationToken ct = default)
            => Task.FromResult<OAuthChannelConfig?>(config);
        public Task<OAuthChannelConfig?> GetDefaultAsync(string provider, string channelType, CancellationToken ct = default)
            => Task.FromResult<OAuthChannelConfig?>(config);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string provider, CancellationToken ct = default)
            => Task.FromResult(new List<OAuthChannelConfig> { config });
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(new List<OAuthChannelConfig> { config });
    }

    private sealed class QueueHttp(params string[] bodies) : HttpMessageHandler
    {
        private int _n;
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Bodies.Add(r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct));
            var body = bodies[Math.Min(_n++, bodies.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static GitHubOAuthProvider Provider(IOAuthChannelConfigProvider cfg, HttpMessageHandler http)
        => new(cfg, new HttpClient(http));

    [Fact]
    public async Task ExchangeCodeSuccessMapsTokens()
    {
        var cfg = new Cfg(new OAuthChannelConfig { AppId = "gh1", AppSecret = "s", RedirectUri = "https://app.test/cb" });
        var p = Provider(cfg, new QueueHttp("""{"access_token":"tok1","openid":"o9","refresh_token":"r1"}"""));
        var r = await p.ExchangeCodeAsync("gh1", "code-1");
        Assert.True(r.Success);
        Assert.Equal("o9", r.OpenId);
        Assert.Equal("tok1", r.AccessToken);
        Assert.Equal("r1", r.RefreshToken);
    }

    [Fact]
    public async Task ExchangeCodeErrorResponseFails()
    {
        var cfg = new Cfg(new OAuthChannelConfig { AppId = "gh1", AppSecret = "s" });
        var p = Provider(cfg, new QueueHttp("""{"error":"bad_verification_code","error_description":"expired"}"""));
        var r = await p.ExchangeCodeAsync("gh1", "code-1");
        Assert.False(r.Success);
        Assert.Contains("bad_verification_code", r.Message);
    }

    [Fact]
    public async Task ExchangeCodeWithoutOpenIdFallsBackToUserInfo()
    {
        var cfg = new Cfg(new OAuthChannelConfig { AppId = "gh1", AppSecret = "s" });
        var http = new QueueHttp(
            """{"access_token":"tok1"}""",
            """{"id":"42","login":"u1"}""");
        var p = Provider(cfg, http);
        var r = await p.ExchangeCodeAsync("gh1", "code-1");
        Assert.True(r.Success);
        Assert.Equal(2, http.Bodies.Count); // token + userinfo
    }

    [Fact]
    public async Task ExchangeCodeMissingConfigThrows()
    {
        var p = new GitHubOAuthProvider(new NullCfg(), new HttpClient(new QueueHttp("{}")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => p.ExchangeCodeAsync("none", "c"));
    }

    private sealed class NullCfg : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string a, CancellationToken ct = default)
            => Task.FromResult<OAuthChannelConfig?>(null);
        public Task<OAuthChannelConfig?> GetDefaultAsync(string p, string c, CancellationToken ct = default)
            => Task.FromResult<OAuthChannelConfig?>(null);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string p, CancellationToken ct = default)
            => Task.FromResult(new List<OAuthChannelConfig>());
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(new List<OAuthChannelConfig>());
    }
}

public sealed class OAuthUserInfoWaveTests
{
    private sealed class Cfg : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string a, CancellationToken ct = default)
            => Task.FromResult<OAuthChannelConfig?>(new OAuthChannelConfig { AppId = a, AppSecret = "s" });
        public Task<OAuthChannelConfig?> GetDefaultAsync(string p, string c, CancellationToken ct = default)
            => Task.FromResult<OAuthChannelConfig?>(null);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string p, CancellationToken ct = default)
            => Task.FromResult(new List<OAuthChannelConfig>());
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(new List<OAuthChannelConfig>());
    }

    private sealed class JsonHttp(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }

    [Fact]
    public async Task GetUserInfoMapsGithubFields()
    {
        var p = new GitHubOAuthProvider(new Cfg(), new HttpClient(new JsonHttp(
            """{"id":"42","login":"u1","avatar_url":"https://av.test/p.png","email":"e@t.test"}""")));
        var r = await p.GetUserInfoAsync("gh1", "tok1");
        Assert.True(r.Success);
        Assert.Equal("42", r.OpenId);
        Assert.Equal("u1", r.NickName);
        Assert.Equal("https://av.test/p.png", r.AvatarUrl);
        Assert.Equal("e@t.test", r.Email);
    }

    [Fact]
    public async Task GetUserInfoMapsQqFields()
    {
        var p = new QQOAuthProvider(new Cfg(), new HttpClient(new JsonHttp(
            """{"openid":"o-qq","nickname":"qn","figureurl_qq_1":"https://av/q.png"}""")));
        var r = await p.GetUserInfoAsync("qq1", "tok1");
        Assert.True(r.Success);
        Assert.Equal("o-qq", r.OpenId);
        Assert.Equal("qn", r.NickName);
    }

    [Fact]
    public void SupportsChannelTypeMatchesConstantCase()
    {
        var p = new GitHubOAuthProvider(new Cfg(), new HttpClient(new JsonHttp("{}")));
        Assert.True(p.SupportsChannelType(OAuthChannelTypes.Web));
        Assert.True(p.SupportsChannelType(OAuthChannelTypes.OpenPlatform));
        Assert.False(p.SupportsChannelType(OAuthChannelTypes.MiniProgram));
    }
}
