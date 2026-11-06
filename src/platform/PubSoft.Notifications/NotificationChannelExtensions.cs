using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Resilience;
using PubSoft.Resilience.Http;

namespace PubSoft.Notifications;

/// <summary>
/// Registration for the built-in notification channels (Teams / DingTalk webhooks).
/// Channels whose configuration section is absent are not registered; a configured-but-empty
/// credential set still registers the channel and fails closed at send time.
/// Vendor-specific integrations (SMS providers, email, push) belong in the host's own
/// <see cref="INotificationChannel"/> implementations, not in this package.
/// </summary>
public static class NotificationChannelExtensions
{
    internal const string TeamsHttpClientName = "PubSoft.Notifications.Teams";
    internal const string DingTalkHttpClientName = "PubSoft.Notifications.DingTalk";

    /// <summary>
    /// Registers every built-in channel that has a configuration section under
    /// <paramref name="notificationsSection"/> (expected subsections: <c>Teams</c>, <c>DingTalk</c>)
    /// and the dispatcher.
    /// </summary>
    public static IServiceCollection AddPubSoftNotificationChannels(
        this IServiceCollection services,
        IConfiguration notificationsSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(notificationsSection);

        if (notificationsSection.GetSection("Teams").Exists())
            services.AddTeamsNotificationChannel(notificationsSection.GetSection("Teams"));
        if (notificationsSection.GetSection("DingTalk").Exists())
            services.AddDingTalkNotificationChannel(notificationsSection.GetSection("DingTalk"));

        services.TryAddSingleton<NotificationDispatcher>();
        return services;
    }

    /// <summary>Registers the Teams webhook channel bound to a configuration section.</summary>
    public static IServiceCollection AddTeamsNotificationChannel(
        this IServiceCollection services, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddTeamsNotificationChannel(o =>
        {
            o.WebhookUrl = section["WebhookUrl"] ?? "";
            o.AllowInsecureUrl = bool.TryParse(section["AllowInsecureUrl"], out var allow) && allow;
        });
    }

    /// <summary>Registers the Teams webhook channel with inline options.</summary>
    public static IServiceCollection AddTeamsNotificationChannel(
        this IServiceCollection services, Action<TeamsWebhookOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<TeamsWebhookOptions>().Configure(configure)
            .Validate(o => IsValidWebhookUrl(o.WebhookUrl, o.AllowInsecureUrl),
                "TeamsWebhookOptions.WebhookUrl must be an absolute https URL; http is allowed only for loopback or with AllowInsecureUrl.");
        return AddTeamsChannelCore(services);
    }

    private static bool IsValidWebhookUrl(string url, bool allowInsecure)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, System.StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, System.StringComparison.Ordinal))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, System.StringComparison.Ordinal) || uri.IsLoopback || allowInsecure;
    }

    private static IServiceCollection AddTeamsChannelCore(IServiceCollection services)
    {
        AddTeamsHttpClient(services);
        services.TryAddSingleton<TeamsWebhookChannel>();
        services.AddSingleton<INotificationChannel>(sp => sp.GetRequiredService<TeamsWebhookChannel>());
        services.TryAddSingleton<NotificationDispatcher>();
        return services;
    }

    /// <summary>Registers the DingTalk webhook channel bound to a configuration section.</summary>
    public static IServiceCollection AddDingTalkNotificationChannel(
        this IServiceCollection services, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddDingTalkNotificationChannel(o =>
        {
            o.WebhookUrl = section["WebhookUrl"] ?? "";
            o.Secret = section["Secret"] ?? "";
            o.AllowInsecureUrl = bool.TryParse(section["AllowInsecureUrl"], out var allow) && allow;
        });
    }

    private static IServiceCollection AddDingTalkChannelCore(IServiceCollection services)
    {
        AddDingTalkHttpClient(services);
        services.TryAddSingleton<DingTalkWebhookChannel>();
        services.AddSingleton<INotificationChannel>(sp => sp.GetRequiredService<DingTalkWebhookChannel>());
        services.TryAddSingleton<NotificationDispatcher>();
        return services;
    }

    /// <summary>Registers the DingTalk webhook channel with inline options.</summary>
    public static IServiceCollection AddDingTalkNotificationChannel(
        this IServiceCollection services, Action<DingTalkWebhookOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<DingTalkWebhookOptions>().Configure(configure)
            .Validate(o => IsValidWebhookUrl(o.WebhookUrl, o.AllowInsecureUrl),
                "DingTalkWebhookOptions.WebhookUrl must be an absolute https URL; http is allowed only for loopback or with AllowInsecureUrl.");
        return AddDingTalkChannelCore(services);
    }

    private static void AddTeamsHttpClient(IServiceCollection services)
        => services.AddHttpClient(TeamsHttpClientName)
            .AddPubSoftHttpResilience(opts =>
            {
                opts.Timeout = TimeSpan.FromSeconds(10);
                opts.PerAttemptTimeout = TimeSpan.FromSeconds(5);
                opts.Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.FromSeconds(1) };
            });

    private static void AddDingTalkHttpClient(IServiceCollection services)
        => services.AddHttpClient(DingTalkHttpClientName)
            .AddPubSoftHttpResilience(opts =>
            {
                opts.Timeout = TimeSpan.FromSeconds(10);
                opts.PerAttemptTimeout = TimeSpan.FromSeconds(5);
                opts.Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.FromSeconds(1) };
            });
}
