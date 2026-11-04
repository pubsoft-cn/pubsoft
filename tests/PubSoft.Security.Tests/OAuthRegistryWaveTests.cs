using Microsoft.Extensions.DependencyInjection;
using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OAuthRegistryWaveTests
{
    [Fact]
    public void AddPubSoftOAuthProvidersRegistersAll()
    {
        var s = new ServiceCollection();
        s.AddPubSoftOAuthProviders();
        Assert.Contains(s, d => d.ServiceType == typeof(IOAuthProviderRegistry));
        Assert.True(s.Count(d => d.ServiceType == typeof(IOAuthProvider)) >= 15); // 19 providers
    }

    private sealed class Cfg : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string a, CancellationToken ct = default) => Task.FromResult<OAuthChannelConfig?>(null);
        public Task<OAuthChannelConfig?> GetDefaultAsync(string p, string ch, CancellationToken ct = default) => Task.FromResult<OAuthChannelConfig?>(null);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string p, CancellationToken ct = default) => Task.FromResult(new List<OAuthChannelConfig>());
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default) => Task.FromResult(new List<OAuthChannelConfig>());
    }

    [Fact]
    public void RegistryResolvesByName()
    {
        var s = new ServiceCollection();
        s.AddSingleton<IOAuthChannelConfigProvider, Cfg>();
        s.AddPubSoftOAuthProviders();
        using var sp = s.BuildServiceProvider();
        var reg = sp.GetRequiredService<IOAuthProviderRegistry>();
        Assert.NotNull(reg.GetProvider("WeChat"));
        Assert.NotNull(reg.GetProvider("Alipay"));
        Assert.NotNull(reg.GetProvider("GitHub"));
        Assert.NotNull(reg.GetProvider("DingTalk"));
        Assert.NotNull(reg.GetProvider("WeCom"));
        Assert.Null(reg.GetProvider("NoSuchProvider"));
        Assert.NotEmpty(reg.GetAllProviders());
    }

    [Fact]
    public void RegistryCaseInsensitive()
    {
        var s = new ServiceCollection();
        s.AddSingleton<IOAuthChannelConfigProvider, Cfg>();
        s.AddPubSoftOAuthProviders();
        using var sp = s.BuildServiceProvider();
        var reg = sp.GetRequiredService<IOAuthProviderRegistry>();
        Assert.NotNull(reg.GetProvider("wechat")); // case-insensitive lookup
        Assert.NotNull(reg.GetProvider("GITHUB"));
    }
}
