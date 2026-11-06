namespace PubSoft.Domain;


public interface IReadOnlyRepository<TEntity, TKey> where TEntity : class
{
    Task<IQueryable<TEntity>> GetQueryableAsync();
    Task<TEntity?> FindAsync(TKey id, CancellationToken cancellationToken = default);
}
