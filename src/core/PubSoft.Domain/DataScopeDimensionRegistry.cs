namespace PubSoft.Domain;

public interface IDataScopeDimensionRegistry
{
    IReadOnlyList<string> Dimensions { get; }
    void Add(string dimension);
    bool Contains(string dimension);
}

public sealed class DataScopeDimensionRegistry : IDataScopeDimensionRegistry
{
    private readonly HashSet<string> _dimensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public IReadOnlyList<string> Dimensions
    {
        get
        {
            lock (_gate)
                return Array.AsReadOnly(_dimensions.ToArray());
        }
    }

    public void Add(string dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimension);
        lock (_gate)
            _dimensions.Add(dimension);
    }

    public bool Contains(string dimension)
    {
        if (string.IsNullOrWhiteSpace(dimension))
            return false;
        lock (_gate)
            return _dimensions.Contains(dimension);
    }
}