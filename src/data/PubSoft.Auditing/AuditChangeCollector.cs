using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;


public sealed class AuditChangeCollector : IAuditChangeCollector
{
    private readonly object _gate = new();
    private readonly List<AuditEntityChange> _changes = [];
    private readonly int _maximumChanges;

    public AuditChangeCollector(AuditChangeCollectorOptions? options = null)
        => _maximumChanges = (options ?? new AuditChangeCollectorOptions()).CloneAndValidate().MaxChanges;

    public IReadOnlyList<AuditEntityChange> GetSnapshot()
    {
        lock (_gate)
            return new ReadOnlyCollection<AuditEntityChange>(_changes.ToArray());
    }

    public void Add(AuditEntityChange change)
        => AddRange([change]);

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

    public void Clear()
    {
        lock (_gate)
            _changes.Clear();
    }
}
