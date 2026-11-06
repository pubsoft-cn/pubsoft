using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


public interface IAuditActorAccessor
{
    AuditActorContext? GetCurrentActor();
}
