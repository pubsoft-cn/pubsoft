using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DingTalkOAuthWaveTests
{
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

    private static DingTalkOAuthProvider P(Http2 http) =>
        new(new Cfg(new OAuthChannelConfig { AppId = "dk1", AppSecret = "s" }), new HttpClient(http));

    [Fact]
    public async Task ExchangeCallsGetuserinfoBycodeWithSignature()
    {
        var http = new Http2("""{"errcode":0,"user_info":{"openid":"o1","unionid":"u1","nick":"n"}}""");
        var r = await P(http).ExchangeCodeAsync("dk1", "tmpcode");
        Assert.True(r.Success);
        Assert.Equal("o1", r.OpenId);
        Assert.Equal("u1", r.UnionId);
        Assert.Contains("getuserinfo_bycode", http.Urls[0]);
        Assert.Contains("signature=", http.Urls[0]);
        Assert.Contains("accessKey=dk1", http.Urls[0]);
    }

    [Fact]
    public async Task ErrcodeFails()
    {
        var r = await P(new Http2("""{"errcode":40078,"errmsg":"invalid tmp_auth_code"}""")).ExchangeCodeAsync("dk1", "bad");
        Assert.False(r.Success);
        Assert.Contains("invalid tmp_auth_code", r.Message);
    }

    [Fact]
    public async Task GetUserInfoIsUnsupportedByDesign()
    {
        var r = await P(new Http2("{}")).GetUserInfoAsync("dk1", "at");
        Assert.False(r.Success);
        Assert.Contains("ExchangeCodeAsync", r.Message ?? "");
    }

    [Fact]
    public void SupportsDingTalkChannels()
    {
        var p = P(new Http2("{}"));
        Assert.True(p.SupportsChannelType("Web"));
    }
}
