using System.Globalization;

namespace PubSoft.Authentication;

/// <summary>
/// Resolves <see cref="CurrentSubject"/> from a <see cref="PrincipalSnapshot"/> with the same
/// strictness as <see cref="AuthenticatedSubjectResolver"/>: exactly one subject claim,
/// at most one of each single-valued claim, all filtered to the trusted issuer.
/// </summary>
public sealed class CurrentSubjectResolver
{
    private readonly SubjectClaimDialect _dialect;
    private readonly string _trustedIssuer;

    /// <summary>Creates the resolver.</summary>
    public CurrentSubjectResolver(SubjectClaimDialect dialect, string trustedIssuer)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentException.ThrowIfNullOrWhiteSpace(dialect.SubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedIssuer);
        _dialect = dialect;
        _trustedIssuer = trustedIssuer;
    }

    /// <summary>Resolves the subject; null when claims are insufficient.</summary>
    public CurrentSubject? Resolve(PrincipalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsAuthenticated)
            return null;

        var subjectIds = snapshot.FindValues(_dialect.SubjectId, _trustedIssuer);
        if (subjectIds.Count != 1 || !TryReadPositiveId(subjectIds[0], out var subjectId))
            return null;

        long? tenantId = null;
        if (_dialect.TenantId is { } tenantClaimType)
        {
            var tenants = snapshot.FindValues(tenantClaimType, _trustedIssuer);
            if (tenants.Count > 1)
                return null;
            if (tenants.Count == 1)
            {
                if (!TryReadPositiveId(tenants[0], out var parsedTenantId))
                    return null;
                tenantId = parsedTenantId;
            }
        }

        if (!TryReadSingle(snapshot, _dialect.Name, out var name)
            || !TryReadSingle(snapshot, _dialect.Email, out var email)
            || !TryReadSingle(snapshot, _dialect.ClientId, out var clientId))
            return null;

        var roles = (IReadOnlyList<string>)(_dialect.Roles is null
            ? []
            : snapshot.FindValues(_dialect.Roles, _trustedIssuer));

        return new CurrentSubject(subjectId, tenantId, name, email, clientId, roles);
    }

    private bool TryReadSingle(PrincipalSnapshot snapshot, string? claimType, out string? value)
    {
        value = null;
        if (claimType is null)
            return true;
        var values = snapshot.FindValues(claimType, _trustedIssuer);
        if (values.Count > 1)
            return false;
        value = values.Count == 1 ? values[0] : null;
        return true;
    }

    private static bool TryReadPositiveId(string value, out long id)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
