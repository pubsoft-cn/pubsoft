using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


/// <summary>Registration for declarative rate-limit policies.</summary>
public static class PubSoftRateLimitPolicyExtensions
{
    /// <summary>Registers one named policy.</summary>
    public static IServiceCollection AddPubSoftRateLimitPolicy(
        this IServiceCollection services,
        Action<PubSoftRateLimitPolicyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new PubSoftRateLimitPolicyOptions();
        configure(options);
        var policy = new PubSoftRateLimitPolicy(options);
        if (services
            .Where(descriptor => descriptor.ServiceType == typeof(PubSoftRateLimitPolicy))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<PubSoftRateLimitPolicy>()
            .Any(existing => string.Equals(existing.Name, policy.Name, StringComparison.Ordinal)))
            throw new ArgumentException($"A rate-limit policy named '{policy.Name}' is already registered.", nameof(configure));

        services.AddSingleton(policy);
        return services;
    }

    /// <summary>Adds the policy rate-limiting middleware.</summary>
    public static IApplicationBuilder UsePubSoftRateLimitPolicies(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PubSoftRateLimitPolicyMiddleware>();
    }
}
