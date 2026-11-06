using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


public interface IAuditSnapshotSink
{
    ValueTask WriteAsync(AuditRequestSnapshot snapshot, CancellationToken cancellationToken = default);
}
