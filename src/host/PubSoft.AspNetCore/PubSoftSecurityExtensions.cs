using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using PubSoft.Security;

namespace PubSoft.AspNetCore;

/// <summary>
/// Security infrastructure registration -- sliding-window rate limiter + PII data masking. 
/// Configuration sections: Security:RateLimiter / Security:DataMasking. 
/// </summary>
public static class PubSoftSecurityExtensions
{
    /// <summary>Registers the security service set.</summary>
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
