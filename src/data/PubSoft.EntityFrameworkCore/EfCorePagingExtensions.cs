using Microsoft.EntityFrameworkCore;
using PubSoft.Core;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Paged-list execution.</summary>
public static class EfCorePagingExtensions
{
    /// <summary>Default upper bound for page sizes.</summary>
    public const int DefaultMaxPageSize = 200;

    /// <summary>
    /// Clamps the paging arguments, then runs the count and page queries into a
    /// <see cref="PagedResultDto{T}"/>: <paramref name="pageIndex"/> floors at 1 and
    /// <paramref name="pageSize"/> clamps to [1, <paramref name="maxPageSize"/>] -
    /// one shared convention instead of per-endpoint drift. Offsets beyond the
    /// <see cref="int"/> range short-circuit to an empty page instead of overflowing
    /// into a negative Skip. Apply <c>OrderBy</c> before calling; unordered Skip/Take
    /// is not deterministic.
    /// </summary>
    public static async Task<PagedResultDto<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source,
        int pageIndex,
        int pageSize,
        int maxPageSize = DefaultMaxPageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPageSize, 0);

        var page = Math.Max(1, pageIndex);
        var size = Math.Clamp(pageSize, 1, maxPageSize);
        var total = await source.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var offset = (long)(page - 1) * size;
        var items = offset <= int.MaxValue
            ? await source.Skip((int)offset).Take(size).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];
        return new PagedResultDto<T>(total, items);
    }
}
