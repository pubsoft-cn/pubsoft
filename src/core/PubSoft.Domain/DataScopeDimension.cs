namespace PubSoft.Domain;

public sealed class DataScopeDimension
{
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

    public string Name { get; }
    public bool IsAll { get; }
    public IReadOnlyList<long> AllowedIds { get; }
    public long? CurrentId { get; }
}