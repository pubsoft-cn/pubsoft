using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

public abstract class DbFirstConfigProvider<TEntity, TInfo> where TEntity : class where TInfo : class
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IConfiguration Configuration;
    protected readonly IMemoryCache Cache;
    protected readonly ILogger? Logger;

    protected DbFirstConfigProvider(IServiceProvider serviceProvider, IConfiguration configuration,
        IMemoryCache cache, ILogger? logger = null)
    {
        ServiceProvider = serviceProvider;
        Configuration = configuration;
        Cache = cache;
        Logger = logger;
    }

    protected abstract string CachePrefix { get; }
    protected abstract string FallbackSectionName { get; }
    protected virtual TimeSpan CacheTtl => TimeSpan.FromMinutes(5);
    protected virtual Expression<Func<TEntity, bool>>? EnabledPredicate => null;

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

    public void InvalidateCache(string key) => Cache.Remove(CachePrefix + key);
    protected abstract string ConfigKey(TInfo info);
    protected abstract Expression<Func<TEntity, bool>> DbPredicate(string key);
    protected abstract TInfo ToInfo(TEntity entity);

    protected virtual TInfo? FindFallbackInfo(string key)
        => FallbackList().FirstOrDefault(i => string.Equals(ConfigKey(i), key, System.StringComparison.Ordinal));

    protected virtual List<TInfo> FallbackList()
        => Configuration.GetSection(FallbackSectionName).Get<List<TInfo>>() ?? new();
}
