using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OAuthProvidersWaveTests
{
    private sealed class NullConfig : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string appId, CancellationToken ct = default)
            => Task.FromResult<OAuthChannelConfig?>(null);
        public Task<OAuthChannelConfig?> GetDefaultAsync(string provider, string channelType, CancellationToken ct = default)
            => Task.FromResult<OAuthChannelConfig?>(null);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string provider, CancellationToken ct = default)
            => Task.FromResult(new List<OAuthChannelConfig>());
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(new List<OAuthChannelConfig>());
    }

    private static HttpClient StubHttp() => new(new System.Net.Http.HttpClientHandler());

    public static IEnumerable<object[]> Providers()
    {
        yield return [new DouyinOAuthProvider(new NullConfig(), StubHttp()), "open.douyin.com"];
        yield return [new ToutiaoOAuthProvider(new NullConfig(), StubHttp()), "open.snssdk.com"];
        yield return [new TaobaoOAuthProvider(new NullConfig(), StubHttp()), "oauth.taobao.com"];
        yield return [new HuaweiOAuthProvider(new NullConfig(), StubHttp()), "oauth-login.cloud.huawei.com"];
        yield return [new XiaomiOAuthProvider(new NullConfig(), StubHttp()), "account.xiaomi.com"];
        yield return [new MicrosoftOAuthProvider(new NullConfig(), StubHttp()), "login.microsoftonline.com"];
        yield return [new GoogleOAuthProvider(new NullConfig(), StubHttp()), "accounts.google.com"];
        yield return [new GitLabOAuthProvider(new NullConfig(), StubHttp()), "gitlab.com"];
        yield return [new QQOAuthProvider(new NullConfig(), StubHttp()), "graph.qq.com"];
        yield return [new GitHubOAuthProvider(new NullConfig(), StubHttp()), "github.com"];
        yield return [new GiteeOAuthProvider(new NullConfig(), StubHttp()), "gitee.com"];
        yield return [new BaiduOAuthProvider(new NullConfig(), StubHttp()), "openapi.baidu.com"];
        yield return [new WeiboOAuthProvider(new NullConfig(), StubHttp()), "api.weibo.com"];
        yield return [new AppleOAuthProvider(new NullConfig(), StubHttp()), "appleid.apple.com"];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task AuthorizeUrlTargetsProviderEndpoint(IOAuthProvider provider, string expectedHost)
    {
        Assert.False(string.IsNullOrWhiteSpace(provider.Provider));
        var result = await provider.GetAuthorizeUrlAsync(new OAuthAuthorizeUrlRequest
        {
            AppId = "app1",
            RedirectUri = "https://app.test/cb",
            CodeChallenge = "cc",
        });
        Assert.True(result.Success, provider.Provider);
        Assert.StartsWith($"https://{expectedHost}", result.AuthorizeUrl);
        Assert.Contains("client_id=", result.AuthorizeUrl);
        Assert.Contains("app1", result.AuthorizeUrl);
    }
}
