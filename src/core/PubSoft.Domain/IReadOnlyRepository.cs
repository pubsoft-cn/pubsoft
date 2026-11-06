namespace PubSoft.Domain;


/// <summary>Read-only repository access for queries that do not track changes.</summary>
public interface IReadOnlyRepository<TEntity, TKey> where TEntity : class
{
    /// <summary>Returns a composable query over the store.</summary>
    Task<IQueryable<TEntity>> GetQueryableAsync();
    /// <summary>Finds one row by primary key, or null.</summary>
    Task<TEntity?> FindAsync(TKey id, CancellationToken cancellationToken = default);
}
