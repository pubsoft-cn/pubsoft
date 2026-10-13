using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;

public static class DbQueryDiagnosticsServiceCollectionExtensions
{
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
        return services;
    }
}