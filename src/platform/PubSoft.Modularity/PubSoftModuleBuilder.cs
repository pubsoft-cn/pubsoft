using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PubSoft.Modularity;

/// <summary>Collects module descriptors for <c>AddPubSoftModules</c>.</summary>
public sealed class PubSoftModuleBuilder
{
    private readonly List<ModuleDescriptor> _descriptors = [];
    private readonly Dictionary<Type, Func<IServiceProvider?, IPubSoftModule>> _factories = new();

    internal ModuleDescriptor[] Descriptors => _descriptors.ToArray();

    internal PubSoftModuleRuntime? Runtime { get; set; }

    /// <inheritdoc cref="Add{T}(bool)"/>
    public PubSoftModuleBuilder Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : IPubSoftModule
        => Add<T>(optional: false);

    /// <summary>
    /// Adds a module by type. During <c>ConfigureServices</c> the module is activated without a
    /// container (parameterless ctor); later phases activate it with <see cref="ActivatorUtilities"/>
    /// so constructor dependencies resolve. Types needing non-DI activation should use the
    /// factory overload.
    /// </summary>
    public PubSoftModuleBuilder Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        bool optional)
        where T : IPubSoftModule
        => Add(ModuleDescriptor.FromType(typeof(T), optional: optional),
            provider => (IPubSoftModule)(provider is { } sp
                ? ActivatorUtilities.CreateInstance(sp, typeof(T))
                : Activator.CreateInstance(typeof(T))!));

    /// <summary>Adds a module with an explicit factory -- the AOT-explicit path.</summary>
    public PubSoftModuleBuilder Add(ModuleDescriptor descriptor, Func<IServiceProvider?, IPubSoftModule> factory)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(factory);
        _descriptors.Add(descriptor);
        _factories[descriptor.ModuleType] = factory;
        return this;
    }

    internal IPubSoftModule Create(Type moduleType)
        => _factories.TryGetValue(moduleType, out var factory)
            ? factory(Runtime?.ServiceProvider)
            : throw new InvalidOperationException($"No factory registered for module '{moduleType.Name}'.");
}
