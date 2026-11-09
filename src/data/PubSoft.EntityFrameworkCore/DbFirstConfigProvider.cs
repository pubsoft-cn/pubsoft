using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>DB-first config provider: reads config rows through the repository, caches them, and falls back to host configuration sections.</summary>
public abstract class DbFirstConfigProvider<TEntity, TInfo> where TEntity : class where TInfo : class
{
    /// <summary>Scope factory source for per-call repository resolution.</summary>
    protected readonly IServiceProvider ServiceProvider;
    /// <summary>Host configuration for section fallbacks.</summary>
    protected readonly IConfiguration Configuration;
    /// <summary>Per-key result cache.</summary>
    protected readonly IMemoryCache Cache;
    /// <summary>Optional logger.</summary>
    protected readonly ILogger? Logger;

    /// <summary>Creates the provider with its dependencies.</summary>
    protected DbFirstConfigProvider(IServiceProvider serviceProvider, IConfiguration configuration,
        IMemoryCache cache, ILogger? logger = null)
    {
        ServiceProvider = serviceProvider;
        Configuration = configuration;
        Cache = cache;
        Logger = logger;
    }

    /// <summary>Cache-key namespace for this provider.</summary>
    protected abstract string CachePrefix { get; }
    /// <summary>Configuration section consulted when no DB row matches.</summary>
    protected abstract string FallbackSectionName { get; }
    /// <summary>Cached-entry lifetime.</summary>
    protected virtual TimeSpan CacheTtl => TimeSpan.FromMinutes(5);
    /// <summary>Optional predicate restricting rows considered enabled.</summary>
    protected virtual Expression<Func<TEntity, bool>>? EnabledPredicate => null;

    /// <summary>Returns the cached/DB/fallback config for <paramref name="key"/>.</summary>
    public async Task<TInfo?> GetConfigAsync(string key, CancellationToken cancellationToken = default)
    {
        var cacheKey = CachePrefix + key;
        if (Cache.TryGetValue(cacheKey, out TInfo? cached))
            return cached;

        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<TEntity, long>>();
        var query = await repo.GetQueryableAsync().ConfigureAwait(false);
        var noTrack = query.AsNoTracking();
        if (EnabledPredicate is { } enabled)
            noTrack = noTrack.Where(enabled);
        var entity = noTrack.Provider is Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider
            ? await noTrack.FirstOrDefaultAsync(DbPredicate(key), cancellationToken).ConfigureAwait(false)
            : noTrack.FirstOrDefault(DbPredicate(key));
        if (entity != null)
        {
            var config = ToInfo(entity);
            Cache.Set(cacheKey, config, CacheTtl);
            return config;
        }

        var fallback = FindFallbackInfo(key);
        if (fallback != null)
            Cache.Set(cacheKey, fallback, CacheTtl);
        return fallback;
    }

    /// <summary>Lists all configs (DB rows first, fallback list appended).</summary>
    public async Task<List<TInfo>> GetAllConfigsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<TEntity, long>>();
        var query = await repo.GetQueryableAsync().ConfigureAwait(false);
        var noTrack = query.AsNoTracking();
        if (EnabledPredicate is { } enabledAll)
            noTrack = noTrack.Where(enabledAll);
        var entities = noTrack.Provider is Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider
            ? await noTrack.ToListAsync(cancellationToken).ConfigureAwait(false)
            : noTrack.ToList();
        var result = entities.Select(ToInfo).ToList();

        foreach (var info in FallbackList())
            if (!result.Any(i => string.Equals(ConfigKey(i), ConfigKey(info), System.StringComparison.Ordinal)))
                result.Add(info);
        return result;
    }

    /// <summary>Evicts the cached config for <paramref name="key"/>.</summary>
    public void InvalidateCache(string key) => Cache.Remove(CachePrefix + key);
    /// <summary>Maps an info record to its lookup key.</summary>
    protected abstract string ConfigKey(TInfo info);
    /// <summary>DB predicate selecting the row for <paramref name="key"/>.</summary>
    protected abstract Expression<Func<TEntity, bool>> DbPredicate(string key);
    /// <summary>Maps the entity row to the info record.</summary>
    protected abstract TInfo ToInfo(TEntity entity);

    /// <summary>Fallback lookup in the configuration section.</summary>
    protected virtual TInfo? FindFallbackInfo(string key)
        => FallbackList().FirstOrDefault(i => string.Equals(ConfigKey(i), key, System.StringComparison.Ordinal));

    /// <summary>Fallback list from the configuration section.</summary>
    protected virtual List<TInfo> FallbackList()
        => Configuration.GetSection(FallbackSectionName).Get<List<TInfo>>() ?? new();
}
