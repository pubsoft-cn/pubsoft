namespace PubSoft.Authentication;

/// <summary>Minimal resolved subject: id + tenant.</summary>
/// <param name="SubjectId">Numeric subject id.</param>
/// <param name="TenantId">Tenant scope.</param>
public sealed record AuthenticatedSubject(long SubjectId, long? TenantId);
