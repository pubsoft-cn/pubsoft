using System.Security.Claims;
using PubSoft.Authentication;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuthenticationWaveTests
{
    private static PrincipalSnapshot Snapshot(bool auth, params PrincipalClaim[] claims)
        => new(auth, claims);

    [Fact]
    public void PrincipalSnapshotFromClaimsPrincipal()
    {
        var id = new ClaimsIdentity([new Claim("sub", "42"), new Claim("tid", "7")], "Bearer");
        var snap = PrincipalSnapshot.FromClaimsPrincipal(new ClaimsPrincipal(id));
        Assert.True(snap.IsAuthenticated);
        Assert.Equal(2, snap.Claims.Count);
        Assert.Contains(snap.Claims, c => c.Type == "sub" && c.Value == "42");
    }

    [Fact]
    public void PrincipalSnapshotAnonymous()
    {
        var snap = PrincipalSnapshot.FromClaimsPrincipal(new ClaimsPrincipal());
        Assert.False(snap.IsAuthenticated);
        Assert.Empty(snap.Claims);
    }

    [Fact]
    public void ResolverCtorValidatesClaimTypes()
    {
        Assert.Throws<ArgumentException>(() => new AuthenticatedSubjectResolver("sub", "iss", "sub")); // same type
        Assert.Throws<ArgumentException>(() => new AuthenticatedSubjectResolver("sub", "iss", " ")); // whitespace tenant
        Assert.Throws<ArgumentNullException>(() => new AuthenticatedSubjectResolver(null!, "iss"));
        Assert.Throws<ArgumentNullException>(() => new AuthenticatedSubjectResolver("sub", null!));
    }

    [Fact]
    public void ResolveAnonymousReturnsNull()
    {
        var r = new AuthenticatedSubjectResolver("sub", "LOCAL AUTHORITY");
        Assert.Null(r.Resolve(Snapshot(false, new PrincipalClaim("sub", "42", "t", "LOCAL AUTHORITY"))));
    }

    [Fact]
    public void ResolveExtractsSubjectAndTenant()
    {
        var r = new AuthenticatedSubjectResolver("sub", "LOCAL AUTHORITY", "tid");
        var claims = new[]
        {
            new PrincipalClaim("sub", "42", ClaimValueTypes.Integer64, "LOCAL AUTHORITY"),
            new PrincipalClaim("tid", "7", ClaimValueTypes.Integer64, "LOCAL AUTHORITY"),
        };
        var s = r.Resolve(Snapshot(true, claims));
        Assert.NotNull(s);
        Assert.Equal(42, s.SubjectId);
        Assert.Equal(7, s.TenantId);
    }

    [Fact]
    public void ResolveRejectsUntrustedIssuer()
    {
        var r = new AuthenticatedSubjectResolver("sub", "trusted-iss", "tid");
        var claims = new[] { new PrincipalClaim("sub", "42", "t", "evil-iss") };
        Assert.Null(r.Resolve(Snapshot(true, claims))); // issuer mismatch → null
    }
}
