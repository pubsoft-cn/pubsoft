using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

public interface IEfCoreRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class
{
    Task<DbSet<TEntity>> GetDbSetAsync();
}