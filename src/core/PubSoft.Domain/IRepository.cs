namespace PubSoft.Domain;

public interface IRepository<TEntity, TKey> where TEntity : class
{
    Task<IQueryable<TEntity>> GetQueryableAsync();
    Task<TEntity> GetAsync(TKey id, CancellationToken cancellationToken = default);
    Task<TEntity?> FindAsync(TKey id, CancellationToken cancellationToken = default);
    Task<TEntity> InsertAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default);
    Task<TEntity> UpdateAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default);
    Task DeleteAsync(TKey id, bool autoSave = false, CancellationToken cancellationToken = default);
    Task DeleteAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default);
    Task UpdateManyAsync(IEnumerable<TEntity> entities, bool autoSave = false, CancellationToken cancellationToken = default);
    Task InsertManyAsync(IEnumerable<TEntity> entities, bool autoSave = false, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
