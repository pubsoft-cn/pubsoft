using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>EF-flavored repository exposing the underlying <see cref="DbSet{TEntity}"/>.</summary>
public interface IEfCoreRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class
{
    /// <summary>Returns the underlying <see cref="DbSet{TEntity}"/>.</summary>
    Task<DbSet<TEntity>> GetDbSetAsync();
}