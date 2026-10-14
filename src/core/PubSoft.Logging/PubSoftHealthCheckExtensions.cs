using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace PubSoft.Logging;

/// <summary>健康检查 DDOS 防护选项</summary>
public sealed class HealthCheckOptions
{
    /// <summary>是否启用 DDOS 防护（缓存+并行+超时）。默认 true。</summary>
    public bool ResilientEnabled { get; set; } = true;

    /// <summary>每个检查项的超时时间（秒）。默认 3。</summary>
    public int TimeoutSeconds { get; set; } = 3;

    /// <summary>健康结果的缓存时长（秒）。默认 5。</summary>
    public int CacheSeconds { get; set; } = 5;

    /// <summary>不健康结果的缓存时长（秒，更短以便快速检测恢复）。默认 1。</summary>
    public int UnhealthyCacheSeconds { get; set; } = 1;

    /// <summary>/health 端点独立限流：每 IP 每窗口最大请求数。默认 60。</summary>
    public int EndpointPermitLimit { get; set; } = 60;

    /// <summary>/health 端点独立限流窗口（秒）。默认 60。</summary>
    public int EndpointWindowSeconds { get; set; } = 60;
}

/// <summary>
/// HealthChecks 服务注册扩展 · HealthCheckAggregator + IAppHealthCheck。
/// </summary>
public static class PubSoftHealthCheckExtensions
{
    /// <summary>
    /// 注册健康检查聚合器（HealthCheckAggregator 单例）。
    /// <para>所有 IAppHealthCheck 实现自动被聚合器收集。</para>
    /// <para>注意：此方法注册的是无防护的基础聚合器。生产环境应使用 AddPubSoftResilientHealthChecks。</para>
    /// </summary>
    public static IServiceCollection AddPubSoftHealthChecks(this IServiceCollection services)
    {
        services.AddSingleton<HealthCheckAggregator>(sp =>
        {
            var checks = sp.GetServices<IAppHealthCheck>();
            return new HealthCheckAggregator(checks);
        });
        return services;
    }

    /// <summary>
    /// 注册 DDOS 防护健康检查聚合器（ResilientHealthCheckAggregator 单例）。
    /// <para>集成缓存 + 并行 + 超时三重防护。</para>
    /// <para>同时注册基础 HealthCheckAggregator（向后兼容）。</para>
    /// </summary>
    public static IServiceCollection AddPubSoftResilientHealthChecks(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new HealthCheckOptions();
        var section = configuration?.GetSection("HealthCheck");
        if (section is not null)
        {
            if (bool.TryParse(section[nameof(HealthCheckOptions.ResilientEnabled)], out var resilient)) options.ResilientEnabled = resilient;
            if (int.TryParse(section[nameof(HealthCheckOptions.TimeoutSeconds)], out var timeout)) options.TimeoutSeconds = timeout;
            if (int.TryParse(section[nameof(HealthCheckOptions.CacheSeconds)], out var cache)) options.CacheSeconds = cache;
            if (int.TryParse(section[nameof(HealthCheckOptions.UnhealthyCacheSeconds)], out var unhealthy)) options.UnhealthyCacheSeconds = unhealthy;
            if (int.TryParse(section[nameof(HealthCheckOptions.EndpointPermitLimit)], out var limit)) options.EndpointPermitLimit = limit;
            if (int.TryParse(section[nameof(HealthCheckOptions.EndpointWindowSeconds)], out var window)) options.EndpointWindowSeconds = window;
        }

        services.AddSingleton(options);

        // 注册 ResilientHealthCheckAggregator（生产用）
        services.AddSingleton<ResilientHealthCheckAggregator>(sp =>
        {
            var checks = sp.GetServices<IAppHealthCheck>();
            return new ResilientHealthCheckAggregator(
                checks,
                timeout: TimeSpan.FromSeconds(options.TimeoutSeconds),
                cacheDuration: TimeSpan.FromSeconds(options.CacheSeconds),
                unhealthyCacheDuration: TimeSpan.FromSeconds(options.UnhealthyCacheSeconds));
        });

        // 同时注册基础聚合器（向后兼容）
        services.AddSingleton<HealthCheckAggregator>(sp =>
        {
            var checks = sp.GetServices<IAppHealthCheck>();
            return new HealthCheckAggregator(checks);
        });

        return services;
    }

    /// <summary>注册单个健康检查。</summary>
    public static IServiceCollection AddPubSoftHealthCheck<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TCheck>(this IServiceCollection services)
        where TCheck : class, IAppHealthCheck
    {
        services.AddSingleton<IAppHealthCheck, TCheck>();
        return services;
    }
}
