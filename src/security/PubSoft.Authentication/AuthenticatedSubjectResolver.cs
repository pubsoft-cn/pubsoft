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
        if (subjectClaimType == tenantClaimType)
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

        var subjectClaims = snapshot.Claims
            .Where(claim => claim.Type == _subjectClaimType && claim.Issuer == _trustedIssuer)
            .ToArray();
        if (subjectClaims.Length != 1 || !TryReadPositiveId(subjectClaims[0].Value, out var subjectId))
            return null;

        long? tenantId = null;
        if (_tenantClaimType is not null)
        {
            var tenantClaims = snapshot.Claims
                .Where(claim => claim.Type == _tenantClaimType && claim.Issuer == _trustedIssuer)
                .ToArray();
            if (tenantClaims.Length > 1)
                return null;
            if (tenantClaims.Length == 1)
            {
                if (!TryReadPositiveId(tenantClaims[0].Value, out var parsedTenantId))
                    return null;
                tenantId = parsedTenantId;
            }
        }

        return new AuthenticatedSubject(subjectId, tenantId);
    }

    private static bool TryReadPositiveId(string value, out long id)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}