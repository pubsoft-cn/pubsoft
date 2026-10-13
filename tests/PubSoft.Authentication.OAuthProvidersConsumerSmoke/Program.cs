using Microsoft.Extensions.DependencyInjection;
using PubSoft.OAuth;

var services = new ServiceCollection();
services.AddSingleton<IOAuthChannelConfigProvider, FakeChannelConfigProvider>();
services.AddPubSoftOAuthProviders();
await using var provider = services.BuildServiceProvider();

var registry = provider.GetRequiredService<IOAuthProviderRegistry>();
var all = registry.GetAllProviders();
if (all.Count != OAuthProviderNames.BuiltIn.Length)
    throw new InvalidOperationException($"expected {OAuthProviderNames.BuiltIn.Length} providers, got {all.Count}");
if (registry.GetProvider("wechat") is not WeChatOAuthProvider)
    throw new InvalidOperationException("registry routing failed");
if (!OAuthProviderNames.IsKnown("ALIPAY"))
    throw new InvalidOperationException("IsKnown failed");

var url = await registry.GetProvider(OAuthProviderNames.WeChat)!
    .GetAuthorizeUrlAsync(new OAuthAuthorizeUrlRequest
    {
        Provider = OAuthProviderNames.WeChat,
        ChannelType = OAuthChannelTypes.OpenPlatform,
        AppId = "wx123",
        RedirectUri = "https://app.example.com/cb",
    });
if (!url.Success || !url.AuthorizeUrl.Contains("qrconnect"))
    throw new InvalidOperationException("authorize URL generation failed");

Console.WriteLine("PubSoft.Authentication.OAuthProviders consumer smoke passed.");

internal sealed class FakeChannelConfigProvider : IOAuthChannelConfigProvider
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
