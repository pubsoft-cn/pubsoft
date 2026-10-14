using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PubSoft.Security;

namespace PubSoft.AspNetCore;

/// <summary>
/// 安全基础设施注册 — 滑动窗口限流器 + PII 数据脱敏。
/// 配置节：Security:RateLimiter / Security:DataMasking。
/// </summary>
public static class PubSoftSecurityExtensions
{
    public static IServiceCollection AddPubSoftSecurity(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<RateLimiterOptions>(configuration.GetSection("Security:RateLimiter"));
            services.Configure<DataMaskingOptions>(configuration.GetSection("Security:DataMasking"));
        }

        // SecurityHeadersMiddleware 构造参数走 DI 解析
        services.TryAddSingleton(sp =>
            sp.GetService<IOptions<SecurityHeadersOptions>>()?.Value ?? new SecurityHeadersOptions());

        services.AddSingleton(sp =>
        {
            var options = configuration?.GetSection("Security:RateLimiter").Get<RateLimiterOptions>() ?? new RateLimiterOptions();
            return new RateLimiter(options);
        });
        services.AddSingleton(sp =>
        {
            var options = configuration?.GetSection("Security:DataMasking").Get<DataMaskingOptions>() ?? new DataMaskingOptions();
            return new DataMaskingService(options);
        });
        return services;
    }
}
