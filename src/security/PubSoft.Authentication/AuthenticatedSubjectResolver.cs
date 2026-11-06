using System.Globalization;

namespace PubSoft.Authentication;

public sealed record AuthenticatedSubject(long SubjectId, long? TenantId);

public sealed class AuthenticatedSubjectResolver
{
    private readonly string _subjectClaimType;
    private readonly string? _tenantClaimType;
    private readonly string _trustedIssuer;

    public AuthenticatedSubjectResolver(
        string subjectClaimType,
        string trustedIssuer,
        string? tenantClaimType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectClaimType);
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedIssuer);
        if (tenantClaimType is not null && string.IsNullOrWhiteSpace(tenantClaimType))
            throw new ArgumentException("Tenant claim type cannot be empty when specified.", nameof(tenantClaimType));
        if (string.Equals(subjectClaimType, tenantClaimType, System.StringComparison.Ordinal))
            throw new ArgumentException("Subject and tenant claim types must differ.", nameof(tenantClaimType));

        _subjectClaimType = subjectClaimType;
        _trustedIssuer = trustedIssuer;
        _tenantClaimType = tenantClaimType;
    }

    public AuthenticatedSubject? Resolve(PrincipalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsAuthenticated)
            return null;

        string? subjectValue = null;
        var subjectCount = 0;
        foreach (var claim in snapshot.Claims)
            if (string.Equals(claim.Type, _subjectClaimType, System.StringComparison.Ordinal) && string.Equals(claim.Issuer, _trustedIssuer, System.StringComparison.Ordinal))
            { subjectValue = claim.Value; subjectCount++; }
        if (subjectCount != 1 || !TryReadPositiveId(subjectValue!, out var subjectId))
            return null;

        long? tenantId = null;
        if (_tenantClaimType is not null)
        {
            string? tenantValue = null;
            var tenantCount = 0;
            foreach (var claim in snapshot.Claims)
                if (string.Equals(claim.Type, _tenantClaimType, System.StringComparison.Ordinal) && string.Equals(claim.Issuer, _trustedIssuer, System.StringComparison.Ordinal))
                { tenantValue = claim.Value; tenantCount++; }
            if (tenantCount > 1)
                return null;
            if (tenantCount == 1)
            {
                if (!TryReadPositiveId(tenantValue!, out var parsedTenantId))
                    return null;
                tenantId = parsedTenantId;
            }
        }

        return new AuthenticatedSubject(subjectId, tenantId);
    }

    private static bool TryReadPositiveId(string value, out long id)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}