using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


/// <summary>Who performed the audited action.</summary>
/// <param name="SubjectId">Subject id.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="IsAdministrator">Whether the actor holds administrative rights.</param>
public sealed record AuditActorContext(string? SubjectId, string? DisplayName, bool? IsAdministrator = null);
