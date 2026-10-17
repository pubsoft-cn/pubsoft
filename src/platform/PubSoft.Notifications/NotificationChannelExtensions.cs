using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Resilience;
using PubSoft.Resilience.Http;

namespace PubSoft.Notifications;

/// <summary>
/// Registration for the built-in notification channels (Teams / DingTalk / Aliyun SMS).
/// Channels whose configuration section is absent are not registered; a configured-but-empty
/// credential set still registers the channel and fails closed at send time.
/// </summary>
public static class NotificationChannelExtensions
{
    internal const string TeamsHttpClientName = "PubSoft.Notifications.Teams";
    internal const string DingTalkHttpClientName = "PubSoft.Notifications.DingTalk";
    internal const string AliyunSmsHttpClientName = "PubSoft.Notifications.AliyunSms";

    /// <summary>
    /// Registers every channel that has a configuration section under <paramref name="notificationsSection"/>
    /// (expected subsections: <c>Teams</c>, <c>DingTalk</c>, <c>AliyunSms</c>) and the dispatcher.
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
        if (notificationsSection.GetSection("AliyunSms").Exists())
            services.AddAliyunSmsNotificationChannel(notificationsSection.GetSection("AliyunSms"));

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
        });
    }

    /// <summary>Registers the Teams webhook channel with inline options.</summary>
    public static IServiceCollection AddTeamsNotificationChannel(
        this IServiceCollection services, Action<TeamsWebhookOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure(configure);
        return AddTeamsChannelCore(services);
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
        services.Configure(configure);
        return AddDingTalkChannelCore(services);
    }

    /// <summary>Registers the Aliyun SMS channel bound to a configuration section.</summary>
    public static IServiceCollection AddAliyunSmsNotificationChannel(
        this IServiceCollection services, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddAliyunSmsNotificationChannel(o =>
        {
            o.AccessKeyId = section["AccessKeyId"] ?? "";
            o.AccessKeySecret = section["AccessKeySecret"] ?? "";
            o.SignName = section["SignName"] ?? "";
            if (section["Endpoint"] is { } endpoint) o.Endpoint = endpoint;
            if (section["RegionId"] is { } regionId) o.RegionId = regionId;
        });
    }

    private static IServiceCollection AddAliyunSmsChannelCore(IServiceCollection services)
    {
        AddAliyunSmsHttpClient(services);
        services.TryAddSingleton<AliyunSmsChannel>();
        services.AddSingleton<INotificationChannel>(sp => sp.GetRequiredService<AliyunSmsChannel>());
        services.TryAddSingleton<NotificationDispatcher>();
        return services;
    }

    /// <summary>Registers the Aliyun SMS channel with inline options.</summary>
    public static IServiceCollection AddAliyunSmsNotificationChannel(
        this IServiceCollection services, Action<AliyunSmsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure(configure);
        return AddAliyunSmsChannelCore(services);
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

    private static void AddAliyunSmsHttpClient(IServiceCollection services)
        => services.AddHttpClient(AliyunSmsHttpClientName)
            .AddPubSoftHttpResilience(opts =>
            {
                opts.Timeout = TimeSpan.FromSeconds(10);
                opts.PerAttemptTimeout = TimeSpan.FromSeconds(5);
                opts.Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.FromSeconds(1) };
                opts.CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 10, OpenTimeout = TimeSpan.FromMinutes(1) };
            });
}
