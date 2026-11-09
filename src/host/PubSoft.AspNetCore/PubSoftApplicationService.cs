using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PubSoft.DependencyInjection;

namespace PubSoft.AspNetCore;

/// <summary>
/// Lightweight ApplicationService base -- implements <see cref="ITransientDependency"/> for convention registration. 
/// <para>No ORM/mapping/localization dependencies -- callers inject their own mapper; <see cref="Logger"/> resolves lazily from <see cref="IServiceProvider"/>.</para>
/// </summary>
public abstract class PubSoftApplicationService : ITransientDependency
{
    private readonly IServiceProvider? _serviceProvider;
    private ILogger? _logger;

    /// <summary>Primary constructor -- takes IServiceProvider via ActivatorUtilities to back lazy Logger resolution.</summary>
    [ActivatorUtilitiesConstructor]
    protected PubSoftApplicationService(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    /// <summary>Parameterless constructor for inheritance paths without DI.</summary>
    protected PubSoftApplicationService() { }

    /// <summary>Logger -- prefers resolving through the injected IServiceProvider, falls back to NullLogger.</summary>
    protected ILogger Logger =>
        _logger ??= _serviceProvider?.GetService<ILoggerFactory>()?.CreateLogger(GetType())
                    ?? NullLogger.Instance;
}
