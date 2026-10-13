namespace PubSoft.Domain;

public sealed class DataScopeAccessEvaluator
{
    private readonly IDataScopeDimensionRegistry _dimensions;

    public DataScopeAccessEvaluator(IDataScopeDimensionRegistry dimensions)
    {
        _dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
    }

    public IReadOnlyList<long>? GetAllowedIds(
        string dimension,
        IEnumerable<DataScopeDimension>? roleScopes)
    {
        if (string.IsNullOrWhiteSpace(dimension) || !_dimensions.Contains(dimension))
            return Array.Empty<long>();

        var matchingScopes = (roleScopes ?? [])
            .Where(scope => string.Equals(scope.Name, dimension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingScopes.Any(scope => scope.IsAll))
            return null;

        return Array.AsReadOnly(matchingScopes
            .SelectMany(scope => scope.AllowedIds)
            .Where(scopeId => scopeId > 0)
            .Distinct()
            .Order()
            .ToArray());
    }

    public bool CanAccess(
        string dimension,
        long scopeId,
        IEnumerable<DataScopeDimension>? roleScopes)
    {
        if (scopeId <= 0)
            return false;

        var allowedIds = GetAllowedIds(dimension, roleScopes);
        return allowedIds is null || allowedIds.Contains(scopeId);
    }
}