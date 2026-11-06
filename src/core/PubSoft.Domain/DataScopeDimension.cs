namespace PubSoft.Domain;

/// <summary>One data-scope dimension (e.g. department, member) with its allowed ids.</summary>
public sealed class DataScopeDimension
{
    /// <summary>Creates a dimension; <paramref name="isAll"/> grants unrestricted access and ignores allowed ids.</summary>
    public DataScopeDimension(
        string name,
        bool isAll,
        IEnumerable<long>? allowedIds = null,
        long? currentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        IsAll = isAll;
        AllowedIds = Array.AsReadOnly(allowedIds?.ToArray() ?? []);
        CurrentId = currentId;
    }

    /// <summary>Dimension key.</summary>
    public string Name { get; }
    /// <summary>Whether the dimension grants unrestricted access.</summary>
    public bool IsAll { get; }
    /// <summary>The ids the caller may see within this dimension.</summary>
    public IReadOnlyList<long> AllowedIds { get; }
    /// <summary>The caller's own id within the dimension, when meaningful.</summary>
    public long? CurrentId { get; }
}