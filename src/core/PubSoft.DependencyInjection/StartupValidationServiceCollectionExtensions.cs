using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.DependencyInjection;

/// <summary>Startup validation registration.</summary>
public static class StartupValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named eager check. The check runs inside a service scope at host
    /// start when <see cref="AddPubSoftStartupValidation"/> is also registered;
    /// without the validator, registered checks are inert.
    /// </summary>
    public static IServiceCollection AddPubSoftStartupCheck(
        this IServiceCollection services, string name, Action<IServiceProvider> check)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(check);
        services.AddSingleton(new StartupCheck(name, check));
        return services;
    }

    /// <summary>
    /// Registers the hosted service that runs every <see cref="StartupCheck"/> once
    /// at startup. Failed checks abort startup by default; set
    /// <see cref="StartupValidationOptions.FailureMode"/> to
    /// <see cref="StartupValidationFailureMode.Warn"/> to log and continue.
    /// </summary>
    public static IServiceCollection AddPubSoftStartupValidation(
        this IServiceCollection services, Action<StartupValidationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new StartupValidationOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, StartupValidationHostedService>());
        return services;
    }
}
