namespace PubSoft.Caching;

/// <summary>Key-value cache contract; class values stored per call site.</summary>
public interface ICache
{
    /// <summary>Reads a value; null on miss/expiry.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    /// <summary>Stores a value with optional expiry.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;
    /// <summary>Deletes a key.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    /// <summary>Whether a key exists unexpired.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    /// <summary>Reads or materializes-and-stores a value.</summary>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;
    /// <summary>Reads many keys in one pass.</summary>
    Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class;
    /// <summary>Stores many values.</summary>
    Task SetManyAsync<T>(IEnumerable<KeyValuePair<string, T>> items, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;
    /// <summary>Deletes many keys.</summary>
    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
    /// <summary>Deletes keys matching a glob pattern.</summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}
