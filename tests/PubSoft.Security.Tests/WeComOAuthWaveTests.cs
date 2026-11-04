using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class WeComOAuthWaveTests
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
            var body = bodies[Math.Min(_i++, bodies.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static WeComOAuthProvider P(Http2 http, string corp) =>
        new(new Cfg(new OAuthChannelConfig { AppId = corp, AppSecret = "sec" }), new HttpClient(http));

    [Fact]
    public async Task ExchangeUsesCorpTokenThenUserinfo()
    {
        var http = new Http2(
            """{"errcode":0,"access_token":"tk1","expires_in":7200}""",
            """{"errcode":0,"userid":"u100","user_ticket":"","openid":"wo1"}""");
        var r = await P(http, "corpA").ExchangeCodeAsync("corpA", "code1");
        Assert.True(r.Success, r.Message);
        Assert.Equal(2, http.Urls.Count);
        Assert.Contains("gettoken?corpid=corpA", http.Urls[0]);
        Assert.Contains("getuserinfo?access_token=tk1", http.Urls[1]);
    }

    [Fact]
    public async Task TokenFetchErrcodeThrows()
    {
        // gettoken errcode throws (not Fail) — token is infra prerequisite, not user-facing failure
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            P(new Http2("""{"errcode":40013,"errmsg":"invalid corpid"}"""), "corpB").ExchangeCodeAsync("corpB", "c"));
        Assert.Contains("invalid corpid", ex.Message);
    }

    [Fact]
    public async Task UserinfoErrcodeFails()
    {
        var r = await P(new Http2(
            """{"errcode":0,"access_token":"tk1","expires_in":7200}""",
            """{"errcode":40029,"errmsg":"invalid code"}"""), "corpC").ExchangeCodeAsync("corpC", "bad");
        Assert.False(r.Success);
    }

    [Fact]
    public async Task GetUserInfoIsExchangeBound()
    {
        var r = await P(new Http2("{}"), "corpD").GetUserInfoAsync("corpD", "at");
        Assert.False(r.Success);
    }
}
