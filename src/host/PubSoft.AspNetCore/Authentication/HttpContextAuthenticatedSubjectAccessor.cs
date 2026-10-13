using PubSoft.Authentication;

namespace PubSoft.AspNetCore.Authentication;

internal sealed class HttpContextAuthenticatedSubjectAccessor(
    IPrincipalSnapshotAccessor snapshotAccessor,
    AuthenticatedSubjectResolver resolver) : IAuthenticatedSubjectAccessor
{
    public AuthenticatedSubject? GetCurrent()
    {
        var snapshot = snapshotAccessor.GetCurrent();
        return snapshot is null ? null : resolver.Resolve(snapshot);
    }
}