using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Registration for query diagnostics.</summary>
public static class DbQueryDiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the diagnostics collector with options, plus a singleton
    /// <see cref="SlowQueryInterceptor"/> at the configured threshold - attach it per
    /// context via <c>options.UsePubSoftQueryDiagnostics(serviceProvider)</c> inside
    /// the AddDbContext lambda. The interceptor is skipped when
    /// <see cref="DbQueryDiagnosticsOptions.Enabled"/> is false.
    /// </summary>
    public static IServiceCollection AddPubSoftDbQueryDiagnostics(
        this IServiceCollection services,
        Action<DbQueryDiagnosticsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new DbQueryDiagnosticsOptions();
        configure?.Invoke(options);
        var validatedOptions = options.CloneAndValidate();

        services.AddSingleton(provider => new DbQueryDiagnostics(
            validatedOptions,
            provider.GetService<ILogger<DbQueryDiagnostics>>()));
        if (validatedOptions.Enabled)
            services.TryAddSingleton(provider => new SlowQueryInterceptor(
                validatedOptions.SlowQueryThreshold,
                provider.GetService<ILogger<SlowQueryInterceptor>>()));
        return services;
    }
}