using System.Security.Claims;
using PubSoft.Authentication;

var principal = new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim("sub", "consumer-user"),
    new Claim("role", "reader"),
    new Claim("role", "auditor")
], "consumer-authentication"));
var snapshot = PrincipalSnapshot.FromClaimsPrincipal(principal);
if (!snapshot.IsAuthenticated
    || snapshot.Claims.Count != 3
    || snapshot.Claims[0].Type != "sub"
    || snapshot.Claims[1].Value != "reader"
    || snapshot.Claims[2].Value != "auditor")
    throw new InvalidOperationException("Authentication package consumer smoke check failed.");

var subjectSnapshot = new PrincipalSnapshot(true,
[
    new PrincipalClaim("sub", "42", ClaimValueTypes.String, "trusted-issuer"),
    new PrincipalClaim("tenant_id", "7", ClaimValueTypes.String, "trusted-issuer")
]);
var subjectResolver = new AuthenticatedSubjectResolver("sub", "trusted-issuer", "tenant_id");
if (subjectResolver.Resolve(subjectSnapshot) != new AuthenticatedSubject(42, 7))
    throw new InvalidOperationException("Authentication subject resolver consumer check failed.");

var untrustedSubjectSnapshot = new PrincipalSnapshot(true,
[
    new PrincipalClaim("sub", "42", ClaimValueTypes.String, "untrusted-issuer")
]);
if (subjectResolver.Resolve(untrustedSubjectSnapshot) is not null)
    throw new InvalidOperationException("Authentication subject resolver accepted an untrusted issuer.");

Console.WriteLine("Isolated PubSoft.Authentication consumer smoke check passed.");