using System.Security.Claims;

namespace PubSoft.Authentication;

/// <summary>Immutable claims snapshot decoupled from ClaimsPrincipal.</summary>
public sealed class PrincipalSnapshot
{
    /// <summary>Creates a snapshot.</summary>
    public PrincipalSnapshot(bool isAuthenticated, IEnumerable<PrincipalClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        IsAuthenticated = isAuthenticated;
        Claims = Array.AsReadOnly(claims.ToArray());
    }

    /// <summary>Whether the principal is authenticated.</summary>
    public bool IsAuthenticated { get; }
    /// <summary>All claims.</summary>
    public IReadOnlyList<PrincipalClaim> Claims { get; }

    /// <summary>The single claim value of <paramref name="claimType"/>, or null when absent or ambiguous.</summary>
    public string? FindValue(string claimType, string? issuer = null)
    {
        var values = FindValues(claimType, issuer);
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>All claim values of <paramref name="claimType"/>, ordinal-compared, optionally issuer-filtered.</summary>
    public IReadOnlyList<string> FindValues(string claimType, string? issuer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);
        var values = new List<string>();
        foreach (var claim in Claims)
            if (string.Equals(claim.Type, claimType, StringComparison.Ordinal)
                && (issuer is null || string.Equals(claim.Issuer, issuer, StringComparison.Ordinal)))
                values.Add(claim.Value);
        return values;
    }

    /// <summary>Builds a snapshot from a ClaimsPrincipal.</summary>
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