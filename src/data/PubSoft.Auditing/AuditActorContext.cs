using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


public sealed record AuditActorContext(string? SubjectId, string? DisplayName, bool? IsAdministrator = null);
