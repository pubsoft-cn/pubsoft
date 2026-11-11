using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Security;

/// <summary>DI registration for token denylists.</summary>
public static class TokenDenylistServiceCollectionExtensions
{
    /// <summary>
    /// Registers the bounded in-process <see cref="ITokenDenylist"/>. Single-instance
    /// deployments only - register a shared implementation (e.g. Redis) for multi-instance.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="maxEntries">Hard cap on retained entries.</param>
    public static IServiceCollection AddPubSoftMemoryTokenDenylist(this IServiceCollection services, int maxEntries = 100_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxEntries, 1_000_000);
        services.TryAddSingleton<ITokenDenylist>(_ => new MemoryTokenDenylist(maxEntries));
        return services;
    }
}
