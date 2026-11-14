using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


/// <summary>Bounded, thread-safe change accumulator honoring <see cref="AuditChangeCollectorOptions"/>.</summary>
public sealed class AuditChangeCollector : IAuditChangeCollector
{
    private readonly object _gate = new();
    private readonly List<AuditEntityChange> _changes = [];
    private readonly int _maximumChanges;

    /// <summary>Creates a collector; null options = defaults.</summary>
    public AuditChangeCollector(AuditChangeCollectorOptions? options = null)
        => _maximumChanges = (options ?? new AuditChangeCollectorOptions()).CloneAndValidate().MaxChanges;

    /// <inheritdoc />
    public IReadOnlyList<AuditEntityChange> GetSnapshot()
    {
        lock (_gate)
            return new ReadOnlyCollection<AuditEntityChange>(_changes.ToArray());
    }

    /// <inheritdoc />
    public void Add(AuditEntityChange change)
        => AddRange([change]);

    /// <inheritdoc />
    public void AddRange(IReadOnlyCollection<AuditEntityChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        lock (_gate)
        {
            if (changes.Count > _maximumChanges - _changes.Count)
                throw new InvalidOperationException($"The audit change limit of {_maximumChanges} was reached.");
            _changes.AddRange(changes);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
            _changes.Clear();
    }
}
