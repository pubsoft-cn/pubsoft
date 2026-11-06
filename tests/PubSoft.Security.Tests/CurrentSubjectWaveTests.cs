using PubSoft.Authentication;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CurrentSubjectWaveTests
{
    private const string Issuer = "iss";

    private static readonly SubjectClaimDialect Dialect = new()
    {
        SubjectId = "sub",
        TenantId = "tid",
        Name = "name",
        Email = "email",
        ClientId = "cid",
        Roles = "role"
    };

    private static PrincipalSnapshot Snap(bool authenticated, params (string Type, string Value)[] claims)
        => new(authenticated, claims.Select(c => new PrincipalClaim(c.Type, c.Value, "http://www.w3.org/2001/XMLSchema#string", Issuer)));

    [Fact]
    public void ResolveMapsDialectClaims()
    {
        var subject = new CurrentSubjectResolver(Dialect, Issuer).Resolve(Snap(true,
            ("sub", "42"), ("tid", "7"), ("name", "n"), ("email", "e@x"), ("cid", "c1"),
            ("role", "admin"), ("role", "ops")));

        Assert.NotNull(subject);
        Assert.Equal(42, subject.SubjectId);
        Assert.Equal(7, subject.TenantId);
        Assert.Equal("n", subject.Name);
        Assert.Equal("e@x", subject.Email);
        Assert.Equal("c1", subject.ClientId);
        Assert.Equal(["admin", "ops"], subject.Roles);
    }

    [Fact]
    public void StrictnessRejectsAmbiguousSinglesAndForeignIssuers()
    {
        var resolver = new CurrentSubjectResolver(Dialect, Issuer);

        Assert.Null(resolver.Resolve(Snap(false, ("sub", "42"))));
        Assert.Null(resolver.Resolve(Snap(true, ("name", "n")))); // no subject id
        Assert.Null(resolver.Resolve(Snap(true, ("sub", "42"), ("sub", "43"))));
        Assert.Null(resolver.Resolve(Snap(true, ("sub", "abc")))); // non-numeric
        Assert.Null(resolver.Resolve(Snap(true, ("sub", "42"), ("tid", "7"), ("name", "a"), ("name", "b"))));

        var foreign = new PrincipalSnapshot(true, [
            new PrincipalClaim("sub", "42", "v", "other-issuer")]);
        Assert.Null(resolver.Resolve(foreign));
    }

    [Fact]
    public void SnapshotFindHelpers()
    {
        var snap = Snap(true, ("role", "a"), ("role", "b"), ("sub", "1"));
        Assert.Equal("1", snap.FindValue("sub"));
        Assert.Null(snap.FindValue("role")); // ambiguous → null
        Assert.Equal(["a", "b"], snap.FindValues("role"));
        Assert.Empty(snap.FindValues("missing"));
    }
}
