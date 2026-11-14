namespace PubSoft.Auditing;

/// <summary>HTTP request facts captured at request start.</summary>
/// <param name="StartedAt">Request start time.</param>
/// <param name="HttpMethod">HTTP verb.</param>
/// <param name="RequestPath">Request path.</param>
/// <param name="ClientIp">Client IP.</param>
/// <param name="UserAgent">Client user agent.</param>
/// <param name="Actor">Acting subject.</param>
public sealed record AuditRequestContext(
    DateTimeOffset StartedAt,
    string? HttpMethod,
    string? RequestPath,
    string? ClientIp = null,
    string? UserAgent = null,
    AuditActorContext? Actor = null);
