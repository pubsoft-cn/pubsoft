using System.Globalization;

namespace PubSoft.Authentication;

public sealed record AuthenticatedSubject(long SubjectId, long? TenantId);
