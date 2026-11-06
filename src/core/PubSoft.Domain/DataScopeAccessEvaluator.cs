namespace PubSoft.Domain;

/// <summary>Evaluates which ids a subject may access within a dimension, given its role scopes.</summary>
public sealed class DataScopeAccessEvaluator
{
    private readonly IDataScopeDimensionRegistry _dimensions;

    /// <summary>Creates an evaluator over the registered dimension names.</summary>
    public DataScopeAccessEvaluator(IDataScopeDimensionRegistry dimensions)
    {
        _dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
    }

    /// <summary>Returns the allowed ids - null when unrestricted, empty when the dimension is unknown.</summary>
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

    /// <summary>Whether <paramref name="scopeId"/> falls inside the subject's allowed set for the dimension.</summary>
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