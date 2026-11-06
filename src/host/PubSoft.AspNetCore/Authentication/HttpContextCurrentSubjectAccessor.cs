using PubSoft.Authentication;

namespace PubSoft.AspNetCore.Authentication;

internal sealed class HttpContextCurrentSubjectAccessor(
    IPrincipalSnapshotAccessor snapshotAccessor,
    CurrentSubjectResolver resolver) : ICurrentSubjectAccessor
{
    public CurrentSubject? GetCurrent()
    {
        var snapshot = snapshotAccessor.GetCurrent();
        return snapshot is null ? null : resolver.Resolve(snapshot);
    }
}
