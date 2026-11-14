using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.AspNetCore;


/// <summary>Registration for the 429 rejection hook.</summary>
public static class PubSoftRateLimitRejectionExtensions
{
    /// <summary>
    /// Registers a host-specific 429 body writer -- e.g. an API error envelope
    /// (<c>{"code":429,"message":"..."}</c>) matching the host's response conventions.
    /// </summary>
    public static IServiceCollection AddPubSoftRateLimitRejection(
        this IServiceCollection services,
        Func<HttpContext, Task> bodyWriter)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new PubSoftRateLimitRejection(bodyWriter));
        return services;
    }
}
