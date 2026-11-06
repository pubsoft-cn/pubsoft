using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.DependencyInjection;


/// <summary>
/// Convention-based service registration — concrete classes carrying a
/// dependency marker are registered for every non-marker public interface they
/// implement plus their own type. Explicit lifetime per marker; no ABP coupling.
/// <para>Fails closed: a class with multiple markers, or abstract/interface types,
/// are skipped silently. The optional caller predicate can narrow the scan further.</para>
/// </summary>
public static class ConventionalServiceCollectionExtensions
{
    /// <summary>
    /// Scans <paramref name="assemblies"/> for concrete classes implementing any dependency marker
    /// and registers each public interface plus the concrete type itself.
    /// </summary>
    public static IServiceCollection AddConventionalServices(
        this IServiceCollection services,
        params Assembly[] assemblies)
        => services.AddConventionalServices(predicate: null, assemblies);

    /// <summary>
    /// Scans <paramref name="assemblies"/> for concrete classes implementing any dependency marker,
    /// narrowing candidates with <paramref name="predicate"/>, and registers each public interface
    /// plus the concrete type itself.
    /// </summary>
    public static IServiceCollection AddConventionalServices(
        this IServiceCollection services,
        Func<Type, bool>? predicate,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (assemblies is null || assemblies.Length == 0)
            throw new ArgumentException("At least one assembly is required.", nameof(assemblies));

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!IsConventionalType(type, predicate))
                    continue;

                var lifetime = ResolveLifetime(type);
                if (lifetime is null)
                    continue;

                var exposed = type.GetCustomAttribute<ExposeServicesAttribute>()?.ServiceTypes;
                var serviceTypes = exposed is { Length: > 0 }
                    ? exposed.Prepend(type).Distinct()
                    : type.GetInterfaces()
                        .Where(i => !IsMarkerInterface(i))
                        .Prepend(type) // self-registration too — handlers/services resolved concretely
                        .Distinct();

                foreach (var serviceType in serviceTypes)
                    services.Add(new ServiceDescriptor(serviceType, type, lifetime.Value));
            }
        }
        return services;
    }

    private static bool IsConventionalType(Type type, Func<Type, bool>? predicate)
        => type is { IsClass: true, IsAbstract: false, IsVisible: true }
            && (predicate is null || predicate(type));

    private static bool IsMarkerInterface(Type i)
        => i == typeof(ITransientDependency) || i == typeof(IScopedDependency) || i == typeof(ISingletonDependency)
            || i == typeof(IDisposable) || i == typeof(IAsyncDisposable);

    private static ServiceLifetime? ResolveLifetime(Type type)
    {
        var transient = typeof(ITransientDependency).IsAssignableFrom(type);
        var scoped = typeof(IScopedDependency).IsAssignableFrom(type);
        var singleton = typeof(ISingletonDependency).IsAssignableFrom(type);
        return (transient, scoped, singleton) switch
        {
            (true, false, false) => ServiceLifetime.Transient,
            (false, true, false) => ServiceLifetime.Scoped,
            (false, false, true) => ServiceLifetime.Singleton,
            _ => null, // unmarked or multiply-marked — skip
        };
    }
}
