using Microsoft.EntityFrameworkCore;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Query helpers for scope-filter control.</summary>
public static class EfCoreQueryExtensions
{
    /// <summary>Strips the tenant and member scope filters for this query.</summary>
    public static IQueryable<TEntity> IgnoreScopeFilters<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class
        => source.IgnoreQueryFilters([PubSoftDbContext.TenantFilter, PubSoftDbContext.MemberFilter]);
}