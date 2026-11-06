namespace PubSoft.Domain;

/// <summary>Registry of dimension names a deployment enables for data-scope grants.</summary>
public interface IDataScopeDimensionRegistry
{
    /// <summary>All registered dimension names.</summary>
    IReadOnlyList<string> Dimensions { get; }
    /// <summary>Registers a dimension name.</summary>
    void Add(string dimension);
    /// <summary>Whether a dimension name is registered.</summary>
    bool Contains(string dimension);
}

/// <summary>Thread-safe in-memory <see cref="IDataScopeDimensionRegistry"/>.</summary>
public sealed class DataScopeDimensionRegistry : IDataScopeDimensionRegistry
{
    private readonly HashSet<string> _dimensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <inheritdoc />
    public IReadOnlyList<string> Dimensions
    {
        get
        {
            lock (_gate)
                return Array.AsReadOnly(_dimensions.ToArray());
        }
    }

    /// <inheritdoc />
    public void Add(string dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimension);
        lock (_gate)
            _dimensions.Add(dimension);
    }

    /// <inheritdoc />
    public bool Contains(string dimension)
    {
        if (string.IsNullOrWhiteSpace(dimension))
            return false;
        lock (_gate)
            return _dimensions.Contains(dimension);
    }
}
