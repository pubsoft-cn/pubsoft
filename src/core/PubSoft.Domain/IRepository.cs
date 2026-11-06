namespace PubSoft.Domain;

/// <summary>Read/write repository for one entity type.</summary>
public interface IRepository<TEntity, TKey> where TEntity : class
{
    /// <summary>Returns a composable query over the store.</summary>
    Task<IQueryable<TEntity>> GetQueryableAsync();
    /// <summary>Gets one row by key; throws when absent.</summary>
    Task<TEntity> GetAsync(TKey id, CancellationToken cancellationToken = default);
    /// <summary>Finds one row by key, or null.</summary>
    Task<TEntity?> FindAsync(TKey id, CancellationToken cancellationToken = default);
    /// <summary>Inserts one row.</summary>
    Task<TEntity> InsertAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default);
    /// <summary>Updates one row.</summary>
    Task<TEntity> UpdateAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default);
    /// <summary>Deletes one row by key.</summary>
    Task DeleteAsync(TKey id, bool autoSave = false, CancellationToken cancellationToken = default);
    /// <summary>Deletes one row.</summary>
    Task DeleteAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default);
    /// <summary>Updates a batch of rows.</summary>
    Task UpdateManyAsync(IEnumerable<TEntity> entities, bool autoSave = false, CancellationToken cancellationToken = default);
    /// <summary>Inserts a batch of rows.</summary>
    Task InsertManyAsync(IEnumerable<TEntity> entities, bool autoSave = false, CancellationToken cancellationToken = default);
    /// <summary>Flushes pending changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
