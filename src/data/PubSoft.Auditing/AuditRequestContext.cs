using System.Collections.ObjectModel;

namespace PubSoft.Auditing;

public sealed record AuditRequestContext(
    DateTimeOffset StartedAt,
    string? HttpMethod,
    string? RequestPath,
    string? ClientIp = null,
    string? UserAgent = null,
    AuditActorContext? Actor = null);
