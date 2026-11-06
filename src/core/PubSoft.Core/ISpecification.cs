using System.Linq.Expressions;

namespace PubSoft.Core;

/// <summary>Query specification contract consumed by repository implementations.</summary>
/// <typeparam name="T">The entity type the specification filters and shapes.</typeparam>
public interface ISpecification<T>
{
    /// <summary>Predicate filtering the result set.</summary>
    Expression<Func<T, bool>>? Criteria { get; }
    /// <summary>Navigation includes as lambda expressions (compiled by the provider).</summary>
    List<Expression<Func<T, object>>> Includes { get; }
    /// <summary>Navigation includes as property paths (e.g. "Orders.Items").</summary>
    List<string> IncludeStrings { get; }
    /// <summary>Ascending sort key.</summary>
    Expression<Func<T, object>>? OrderBy { get; }
    /// <summary>Descending sort key.</summary>
    Expression<Func<T, object>>? OrderByDescending { get; }
    /// <summary>Grouping key.</summary>
    Expression<Func<T, object>>? GroupBy { get; }
    /// <summary>Maximum rows returned when paging is enabled.</summary>
    int Take { get; }
    /// <summary>Rows skipped when paging is enabled.</summary>
    int Skip { get; }
    /// <summary>Whether Skip/Take apply.</summary>
    bool IsPagingEnabled { get; }
}

/// <summary>Base class for building specifications fluently inside derived constructors.</summary>
public abstract class Specification<T> : ISpecification<T> where T : class
{
    /// <summary>Creates a specification without a criteria.</summary>
    protected Specification() { }
    /// <summary>Creates a specification filtering by <paramref name="criteria"/>.</summary>
    protected Specification(Expression<Func<T, bool>> criteria) { Criteria = criteria; }

    /// <inheritdoc />
    public Expression<Func<T, bool>>? Criteria { get; private set; }
    /// <inheritdoc />
    public List<Expression<Func<T, object>>> Includes { get; } = [];
    /// <inheritdoc />
    public List<string> IncludeStrings { get; } = [];
    /// <inheritdoc />
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    /// <inheritdoc />
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    /// <inheritdoc />
    public Expression<Func<T, object>>? GroupBy { get; private set; }
    /// <inheritdoc />
    public int Take { get; private set; }
    /// <inheritdoc />
    public int Skip { get; private set; }
    /// <inheritdoc />
    public bool IsPagingEnabled { get; private set; }

    /// <summary>Adds a navigation include by expression.</summary>
    protected void AddInclude(Expression<Func<T, object>> includeExpression) => Includes.Add(includeExpression);
    /// <summary>Adds a navigation include by property path.</summary>
    protected void AddInclude(string includeString) => IncludeStrings.Add(includeString);
    /// <summary>Sets the ascending sort key.</summary>
    protected void ApplyOrderBy(Expression<Func<T, object>> orderByExpression) => OrderBy = orderByExpression;
    /// <summary>Sets the descending sort key.</summary>
    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescendingExpression) => OrderByDescending = orderByDescendingExpression;
    /// <summary>Sets the grouping key.</summary>
    protected void ApplyGroupBy(Expression<Func<T, object>> groupByExpression) => GroupBy = groupByExpression;
    /// <summary>Enables paging with the given offset and page size.</summary>
    protected void ApplyPaging(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }
}
