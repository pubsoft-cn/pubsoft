using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AlipayOAuthWaveTests
{
    private sealed class Cfg(OAuthChannelConfig? c) : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string a, CancellationToken ct = default) => Task.FromResult(c);
        public Task<OAuthChannelConfig?> GetDefaultAsync(string p, string ch, CancellationToken ct = default) => Task.FromResult(c);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string p, CancellationToken ct = default) => Task.FromResult(c is null ? new() : new List<OAuthChannelConfig> { c });
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default) => Task.FromResult(c is null ? new() : new List<OAuthChannelConfig> { c });
    }

    private static AlipayOAuthProvider P(OAuthChannelConfig? c) =>
        new(new Cfg(c), new HttpClient(new HttpClientHandler()));

    [Fact]
    public void SupportsMiniProgramAndWeb()
    {
        var p = P(null);
        Assert.True(p.SupportsChannelType("MiniProgram"));
        Assert.True(p.SupportsChannelType("Web"));
        Assert.False(p.SupportsChannelType("OpenPlatform"));
    }

    [Fact]
    public async Task AuthorizeUrlEncodesParams()
    {
        var r = await P(null).GetAuthorizeUrlAsync(new OAuthAuthorizeUrlRequest
        { AppId = "a&b", RedirectUri = "https://cb/?x=1", State = "s 1" });
        Assert.True(r.Success);
        Assert.Contains("app_id=a%26b", r.AuthorizeUrl);
        Assert.Contains("redirect_uri=https%3a%2f%2fcb%2f%3fx%3d1", r.AuthorizeUrl.ToLowerInvariant());
        Assert.Contains("state=s%201", r.AuthorizeUrl);
        Assert.Contains("scope=auth_user", r.AuthorizeUrl);
    }

    [Fact]
    public async Task ResolveConfigThrowsOnEmptyAppId()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            P(null).ExchangeCodeAsync("  ", "code"));

    [Fact]
    public async Task ResolveConfigThrowsOnMissingConfig()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            P(null).ExchangeCodeAsync("no-cfg", "code"));
}
