using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Authorization;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Registration for EF-backed PubSoft services.</summary>
public static class EfCoreServiceCollectionExtensions
{
    /// <summary>Registers the EF permission-grant provider.</summary>
    public static IServiceCollection AddPubSoftEfCorePermissionGrantProvider<TDbContext>(this IServiceCollection services)
        where TDbContext : PubSoftDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IPermissionGrantProvider, EfCorePermissionGrantProvider<TDbContext>>();
        return services;
    }

    /// <summary>Registers the EF permission-grant manager.</summary>
    public static IServiceCollection AddPubSoftEfCorePermissionGrantManager<TDbContext>(this IServiceCollection services)
        where TDbContext : PubSoftDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<EfCorePermissionGrantManager<TDbContext>>();
        return services;
    }

    /// <summary>Registers the EF role-data-scope provider.</summary>
    public static IServiceCollection AddPubSoftEfCoreRoleDataScopeProvider<TDbContext>(this IServiceCollection services)
        where TDbContext : PubSoftDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IRoleDataScopeProvider, EfCoreRoleDataScopeProvider<TDbContext>>();
        return services;
    }

    /// <summary>Registers the EF repository for the entity type.</summary>
    public static IServiceCollection AddPubSoftRepository<TEntity, TKey, TDbContext>(this IServiceCollection services)
        where TEntity : class
        where TDbContext : PubSoftDbContext
    {
        services.AddScoped<EfCoreRepository<TEntity, TKey, TDbContext>>();
        services.AddScoped<IRepository<TEntity, TKey>>(
            provider => provider.GetRequiredService<EfCoreRepository<TEntity, TKey, TDbContext>>());
        services.AddScoped<IReadOnlyRepository<TEntity, TKey>>(
            provider => provider.GetRequiredService<EfCoreRepository<TEntity, TKey, TDbContext>>());
        services.AddScoped<IEfCoreRepository<TEntity, TKey>>(
            provider => provider.GetRequiredService<EfCoreRepository<TEntity, TKey, TDbContext>>());
        return services;
    }

    /// <summary>Registers the unit of work.</summary>
    public static IServiceCollection AddPubSoftUnitOfWork<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IPubSoftUnitOfWork<TDbContext>, PubSoftUnitOfWork<TDbContext>>();
        return services;
    }

    /// <summary>
    /// Registers the <typeparamref name="TDbContext"/> itself as an <see cref="IPubSoftSaveParticipant"/>
    /// discovered by request-end save coordination (for example PubSoft.AspNetCore save middleware).
    /// </summary>
    /// <remarks>
    /// Resolving participants may construct the context at save time even when the request never used it;
    /// a save on an unchanged context is a no-op. Register each context once -- either here or via
    /// <see cref="AddPubSoftRequestUnitOfWork{TDbContext}"/>, not both.
    /// </remarks>
    /// <summary>Registers a save participant.</summary>
    public static IServiceCollection AddPubSoftSaveParticipant<TDbContext>(this IServiceCollection services)
        where TDbContext : PubSoftDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IPubSoftSaveParticipant>(provider => provider.GetRequiredService<TDbContext>());
        return services;
    }

    /// <summary>
    /// Registers a scoped unit of work over <typeparamref name="TDbContext"/> and enrolls the context as
    /// an <see cref="IPubSoftSaveParticipant"/> for request-end save coordination.
    /// </summary>
    public static IServiceCollection AddPubSoftRequestUnitOfWork<TDbContext>(this IServiceCollection services)
        where TDbContext : PubSoftDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPubSoftUnitOfWork<TDbContext>();
        services.AddScoped<IPubSoftSaveParticipant>(provider => provider.GetRequiredService<TDbContext>());
        return services;
    }

    /// <summary>Registers a SaveChanges hook.</summary>
    public static IServiceCollection AddPubSoftSaveChangesHook<TDbContext, THook>(this IServiceCollection services)
        where TDbContext : PubSoftDbContext
        where THook : class, ISaveChangesHook
    {
        services.AddScoped<ISaveChangesHook, THook>();
        return services;
    }

    /// <summary>Registers the audit change-capture hook.</summary>
    public static IServiceCollection AddPubSoftAuditChangeCapture(
        this IServiceCollection services,
        Action<AuditChangeCollectorOptions>? configure = null,
        Action<AuditScopeOptions>? configureScope = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new AuditChangeCollectorOptions();
        configure?.Invoke(options);
        var validatedOptions = options.CloneAndValidate();
        var scopeOptions = new AuditScopeOptions();
        configureScope?.Invoke(scopeOptions);

        services.AddScoped(_ => validatedOptions);
        services.AddScoped(_ => scopeOptions);
        services.AddScoped<IAuditChangeCollector, AuditChangeCollector>();
        services.AddScoped<IAuditScope, AuditScope>();
        services.AddScoped<ISaveChangesHook, AuditChangeCaptureHook>();
        return services;
    }
}