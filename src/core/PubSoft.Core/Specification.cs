using System.Linq.Expressions;

namespace PubSoft.Core;

public interface ISpecification<T>
{
    Expression<Func<T, bool>>? Criteria { get; }
    List<Expression<Func<T, object>>> Includes { get; }
    List<string> IncludeStrings { get; }
    Expression<Func<T, object>>? OrderBy { get; }
    Expression<Func<T, object>>? OrderByDescending { get; }
    Expression<Func<T, object>>? GroupBy { get; }
    int Take { get; }
    int Skip { get; }
    bool IsPagingEnabled { get; }
}

public abstract class Specification<T> : ISpecification<T> where T : class
{
    protected Specification() { }
    protected Specification(Expression<Func<T, bool>> criteria) { Criteria = criteria; }

    public Expression<Func<T, bool>>? Criteria { get; private set; }
    public List<Expression<Func<T, object>>> Includes { get; } = [];
    public List<string> IncludeStrings { get; } = [];
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    public Expression<Func<T, object>>? GroupBy { get; private set; }
    public int Take { get; private set; }
    public int Skip { get; private set; }
    public bool IsPagingEnabled { get; private set; }

    protected void AddInclude(Expression<Func<T, object>> includeExpression) => Includes.Add(includeExpression);
    protected void AddInclude(string includeString) => IncludeStrings.Add(includeString);
    protected void ApplyOrderBy(Expression<Func<T, object>> orderByExpression) => OrderBy = orderByExpression;
    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescendingExpression) => OrderByDescending = orderByDescendingExpression;
    protected void ApplyGroupBy(Expression<Func<T, object>> groupByExpression) => GroupBy = groupByExpression;
    protected void ApplyPaging(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }
}
