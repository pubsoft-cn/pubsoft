using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;


public interface IAuditChangeCollector
{
    IReadOnlyList<AuditEntityChange> GetSnapshot();
    void Add(AuditEntityChange change);
    void AddRange(IReadOnlyCollection<AuditEntityChange> changes);
    void Clear();
}
