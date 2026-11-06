using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Resilience;
using PubSoft.Resilience.Http;

namespace PubSoft.OAuth;

/// <summary>
/// DI registration for the unified OAuth providers.
/// Usage: services.AddPubSoftOAuthProviders()
/// Registers all built-in providers + <see cref="IOAuthProviderRegistry"/> + <see cref="IWeChatAccessTokenProvider"/>.
/// Channel credentials come from the host-supplied <see cref="IOAuthChannelConfigProvider"/>.
/// </summary>
public static class OAuthProviderServiceCollectionExtensions
{
    /// <summary>Registers all built-in OAuth providers and the registry.</summary>
    public static IServiceCollection AddPubSoftOAuthProviders(
        this IServiceCollection services,
        Action<HttpResilienceOptions>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddProvider<WeChatOAuthProvider>(services, configureResilience);
        AddProvider<AlipayOAuthProvider>(services, configureResilience);
        AddProvider<WeComOAuthProvider>(services, configureResilience);
        AddProvider<DingTalkOAuthProvider>(services, configureResilience);

        AddProvider<QQOAuthProvider>(services, configureResilience);
        AddProvider<GitHubOAuthProvider>(services, configureResilience);
        AddProvider<GiteeOAuthProvider>(services, configureResilience);
        AddProvider<BaiduOAuthProvider>(services, configureResilience);
        AddProvider<WeiboOAuthProvider>(services, configureResilience);
        AddProvider<AppleOAuthProvider>(services, configureResilience);

        AddProvider<DouyinOAuthProvider>(services, configureResilience);
        AddProvider<ToutiaoOAuthProvider>(services, configureResilience);
        AddProvider<TaobaoOAuthProvider>(services, configureResilience);
        AddProvider<HuaweiOAuthProvider>(services, configureResilience);
        AddProvider<XiaomiOAuthProvider>(services, configureResilience);

        AddProvider<MicrosoftOAuthProvider>(services, configureResilience);
        AddProvider<GoogleOAuthProvider>(services, configureResilience);
        AddProvider<GitLabOAuthProvider>(services, configureResilience);
        AddProvider<TwitterOAuthProvider>(services, configureResilience);

        services.TryAddSingleton<IOAuthProviderRegistry, OAuthProviderRegistry>();

        // WeChat global access_token provider (cached; shared by WeChatOAuthProvider and host code).
        services.AddHttpClient<WeChatAccessTokenProvider>()
            .AddPubSoftHttpResilience(opts => ConfigureDefault(opts, configureResilience));
        services.TryAddTransient<IWeChatAccessTokenProvider>(sp => sp.GetRequiredService<WeChatAccessTokenProvider>());

        return services;
    }

    /// <summary>Registers all providers plus the host's <see cref="IOAuthChannelConfigProvider"/> implementation.</summary>
    public static IServiceCollection AddPubSoftOAuthProviders<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfigProvider>(
        this IServiceCollection services,
        Action<HttpResilienceOptions>? configureResilience = null)
        where TConfigProvider : class, IOAuthChannelConfigProvider
    {
        services.TryAddTransient<IOAuthChannelConfigProvider, TConfigProvider>();
        return services.AddPubSoftOAuthProviders(configureResilience);
    }

    private static IServiceCollection AddProvider<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        IServiceCollection services,
        Action<HttpResilienceOptions>? configureResilience)
        where T : class, IOAuthProvider
    {
        services.AddHttpClient<T>()
            .AddPubSoftHttpResilience(opts => ConfigureDefault(opts, configureResilience));
        // AddHttpClient<T> only registers the concrete type - the registry resolves via IEnumerable<IOAuthProvider>.
        services.AddTransient<IOAuthProvider>(sp => sp.GetRequiredService<T>());
        return services;
    }

    private static void ConfigureDefault(HttpResilienceOptions opts, Action<HttpResilienceOptions>? configureResilience)
    {
        opts.Timeout = TimeSpan.FromSeconds(8);
        opts.PerAttemptTimeout = TimeSpan.FromSeconds(5);
        opts.Retry = new RetryPolicy { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(300) };
        opts.CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 5, OpenTimeout = TimeSpan.FromSeconds(30) };
        configureResilience?.Invoke(opts);
    }
}
