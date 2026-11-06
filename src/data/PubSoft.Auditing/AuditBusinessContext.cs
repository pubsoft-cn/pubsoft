using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


public sealed record AuditBusinessContext(
    string? Category = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    string? TargetName = null,
    string? Detail = null);
