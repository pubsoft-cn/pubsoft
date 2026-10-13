using System.Security.Claims;

namespace PubSoft.Authentication;

public sealed class PrincipalSnapshot
{
    public PrincipalSnapshot(bool isAuthenticated, IEnumerable<PrincipalClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        IsAuthenticated = isAuthenticated;
        Claims = Array.AsReadOnly(claims.ToArray());
    }

    public bool IsAuthenticated { get; }
    public IReadOnlyList<PrincipalClaim> Claims { get; }

    public static PrincipalSnapshot FromClaimsPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new PrincipalSnapshot(
            principal.Identities.Any(identity => identity.IsAuthenticated),
            principal.Claims.Select(claim => new PrincipalClaim(
                claim.Type,
                claim.Value,
                claim.ValueType,
                claim.Issuer)));
    }
}