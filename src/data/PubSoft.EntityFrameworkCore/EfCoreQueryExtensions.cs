using Microsoft.EntityFrameworkCore;

namespace PubSoft.EntityFrameworkCore;

public static class EfCoreQueryExtensions
{
    public static IQueryable<TEntity> IgnoreScopeFilters<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class
        => source.IgnoreQueryFilters([PubSoftDbContext.TenantFilter, PubSoftDbContext.MemberFilter]);
}