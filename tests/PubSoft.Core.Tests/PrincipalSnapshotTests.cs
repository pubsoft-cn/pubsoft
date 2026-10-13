using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore.Authentication;
using PubSoft.Authentication;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class PrincipalSnapshotTests
{
    [Fact]
    public void FromClaimsPrincipalPreservesIdentityAuthenticationAndClaimOrder()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
            [
                new Claim("sub", "user-7", ClaimValueTypes.String, "issuer-a"),
                new Claim("role", "reader"),
                new Claim("sub", "user-alias")
            ], "bearer"),
            new ClaimsIdentity([new Claim("tenant_id", "4")])
        ]);

        var snapshot = PrincipalSnapshot.FromClaimsPrincipal(principal);

        Assert.True(snapshot.IsAuthenticated);
        Assert.Equal(["sub", "role", "sub", "tenant_id"], snapshot.Claims.Select(claim => claim.Type));
        Assert.Equal(["user-7", "reader", "user-alias", "4"], snapshot.Claims.Select(claim => claim.Value));
        Assert.Equal("issuer-a", snapshot.Claims[0].Issuer);
    }

    [Fact]
    public void ConstructorCopiesClaimsAndExposesReadOnlyCollection()
    {
        var claims = new List<PrincipalClaim> { new("sub", "user-7", ClaimValueTypes.String, "issuer") };

        var snapshot = new PrincipalSnapshot(false, claims);
        claims.Add(new PrincipalClaim("role", "admin", ClaimValueTypes.String, "issuer"));

        Assert.False(snapshot.IsAuthenticated);
        Assert.Single(snapshot.Claims);
        Assert.Throws<NotSupportedException>(() => ((IList<PrincipalClaim>)snapshot.Claims)
            .Add(new PrincipalClaim("role", "admin", ClaimValueTypes.String, "issuer")));
    }

    [Fact]
    public void AnonymousPrincipalIsNotPromotedToAuthenticated()
    {
        var snapshot = PrincipalSnapshot.FromClaimsPrincipal(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(snapshot.IsAuthenticated);
        Assert.Empty(snapshot.Claims);
    }

    [Fact]
    public void AuthenticatedSubjectResolverRequiresConfiguredIssuerAndUniquePositiveIds()
    {
        var resolver = new AuthenticatedSubjectResolver("sub", "trusted", "tenant_id");
        var snapshot = new PrincipalSnapshot(true,
        [
            new PrincipalClaim("sub", "999", ClaimValueTypes.String, "untrusted"),
            new PrincipalClaim("sub", "17", ClaimValueTypes.String, "trusted"),
            new PrincipalClaim("tenant_id", "4", ClaimValueTypes.String, "trusted")
        ]);

        var subject = resolver.Resolve(snapshot);

        Assert.Equal(new AuthenticatedSubject(17, 4), subject);
    }

    [Theory]
    [InlineData(false, "17", "trusted")]
    [InlineData(true, "0", "trusted")]
    [InlineData(true, "not-an-id", "trusted")]
    [InlineData(true, "17", "untrusted")]
    public void AuthenticatedSubjectResolverFailsClosedForInvalidSubject(bool isAuthenticated, string value, string issuer)
    {
        var resolver = new AuthenticatedSubjectResolver("sub", "trusted");
        var snapshot = new PrincipalSnapshot(isAuthenticated,
            [new PrincipalClaim("sub", value, ClaimValueTypes.String, issuer)]);

        Assert.Null(resolver.Resolve(snapshot));
    }

    [Fact]
    public void AuthenticatedSubjectResolverRejectsAmbiguousSubjectOrTenantClaims()
    {
        var resolver = new AuthenticatedSubjectResolver("sub", "trusted", "tenant_id");
        var duplicateSubjects = new PrincipalSnapshot(true,
        [
            new PrincipalClaim("sub", "17", ClaimValueTypes.String, "trusted"),
            new PrincipalClaim("sub", "18", ClaimValueTypes.String, "trusted")
        ]);
        var duplicateTenants = new PrincipalSnapshot(true,
        [
            new PrincipalClaim("sub", "17", ClaimValueTypes.String, "trusted"),
            new PrincipalClaim("tenant_id", "4", ClaimValueTypes.String, "trusted"),
            new PrincipalClaim("tenant_id", "5", ClaimValueTypes.String, "trusted")
        ]);

        Assert.Null(resolver.Resolve(duplicateSubjects));
        Assert.Null(resolver.Resolve(duplicateTenants));
    }

    [Fact]
    public void AuthenticatedSubjectResolverRejectsInvalidClaimMappingConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new AuthenticatedSubjectResolver(" ", "issuer"));
        Assert.Throws<ArgumentException>(() => new AuthenticatedSubjectResolver("sub", "issuer", " "));
        Assert.Throws<ArgumentException>(() => new AuthenticatedSubjectResolver("same", "issuer", "same"));
    }

    [Fact]
    public void AspNetAccessorReturnsNullWithoutRequestAndReadsCurrentPrincipalOnEachCall()
    {
        var services = new ServiceCollection();
        services.AddPrincipalSnapshotAccessor();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var snapshotAccessor = scope.ServiceProvider.GetRequiredService<IPrincipalSnapshotAccessor>();

        Assert.Null(snapshotAccessor.GetCurrent());

        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "first")], "test"))
        };
        Assert.Equal("first", snapshotAccessor.GetCurrent()!.Claims.Single().Value);

        httpContextAccessor.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "second")], "test"));
        Assert.Equal("second", snapshotAccessor.GetCurrent()!.Claims.Single().Value);
    }

    [Fact]
    public void AspNetAuthenticatedSubjectAccessorUsesExplicitTrustedMapping()
    {
        var services = new ServiceCollection();
        services.AddAuthenticatedSubjectAccessor("sub", "trusted-issuer", "tenant_id");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var subjectAccessor = scope.ServiceProvider.GetRequiredService<IAuthenticatedSubjectAccessor>();

        Assert.Null(subjectAccessor.GetCurrent());

        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "42", ClaimValueTypes.String, "trusted-issuer"),
                new Claim("tenant_id", "7", ClaimValueTypes.String, "trusted-issuer")
            ], "bearer"))
        };
        Assert.Equal(new AuthenticatedSubject(42, 7), subjectAccessor.GetCurrent());

        httpContextAccessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "42", ClaimValueTypes.String, "untrusted-issuer")], "bearer"));
        Assert.Null(subjectAccessor.GetCurrent());
    }
}