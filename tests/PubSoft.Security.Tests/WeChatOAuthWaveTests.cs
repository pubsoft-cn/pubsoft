using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class WeChatOAuthWaveTests
{
    private sealed class TokenProvider : IWeChatAccessTokenProvider
    {
        public Task<WeChatAccessTokenResult> GetAsync(string appId, CancellationToken ct = default)
            => Task.FromResult(new WeChatAccessTokenResult { Success = true, AccessToken = "t1" });
    }

    private sealed class Cfg(OAuthChannelConfig c) : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string a, CancellationToken ct = default) => Task.FromResult<OAuthChannelConfig?>(c);
        public Task<OAuthChannelConfig?> GetDefaultAsync(string p, string ch, CancellationToken ct = default) => Task.FromResult<OAuthChannelConfig?>(c);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string p, CancellationToken ct = default) => Task.FromResult(new List<OAuthChannelConfig> { c });
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default) => Task.FromResult(new List<OAuthChannelConfig> { c });
    }

    private sealed class Http2(string body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Urls.Add(r.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static OAuthChannelConfig Conf => new() { AppId = "wx1", AppSecret = "s", ChannelType = "MiniProgram" };

    [Fact]
    public async Task MiniProgramUsesJsCode2Session()
    {
        var http = new Http2("""{"openid":"o1","unionid":"u1","session_key":"sk"}""");
        var p = new WeChatOAuthProvider(new Cfg(Conf), new TokenProvider(), new HttpClient(http));
        var r = await p.ExchangeCodeAsync("wx1", "jc1", "MiniProgram");
        Assert.True(r.Success);
        Assert.Equal("o1", r.OpenId);
        Assert.Equal("u1", r.UnionId);
        Assert.Contains("jscode2session", http.Urls[0]);
        Assert.Contains("js_code=jc1", http.Urls[0]);
    }

    [Fact]
    public async Task WebChannelUsesSnsOauth2()
    {
        var http = new Http2("""{"openid":"o2","unionid":"u2","access_token":"t"}""");
        var p = new WeChatOAuthProvider(new Cfg(new OAuthChannelConfig { AppId = "wx2", AppSecret = "s", ChannelType = "Web" }), new TokenProvider(), new HttpClient(http));
        var r = await p.ExchangeCodeAsync("wx2", "c1", "Web");
        Assert.True(r.Success);
        Assert.Equal("o2", r.OpenId);
        Assert.Contains("oauth2/access_token", http.Urls[0]);
    }

    [Fact]
    public async Task ErrcodeFails()
    {
        var p = new WeChatOAuthProvider(new Cfg(Conf), new TokenProvider(), new HttpClient(new Http2("""{"errcode":40029,"errmsg":"invalid code"}""")));
        var r = await p.ExchangeCodeAsync("wx1", "bad", "MiniProgram");
        Assert.False(r.Success);
        Assert.Contains("invalid code", r.Message);
    }

    [Fact]
    public void SupportsAllWeChatChannels()
    {
        var p = new WeChatOAuthProvider(new Cfg(Conf), new TokenProvider(), new HttpClient(new Http2("{}")));
        Assert.True(p.SupportsChannelType("MiniProgram"));
        Assert.True(p.SupportsChannelType("OfficialAccount"));
        Assert.True(p.SupportsChannelType("OpenPlatform"));
        Assert.True(p.SupportsChannelType("Web"));
        Assert.False(p.SupportsChannelType("Unknown"));
    }
}
