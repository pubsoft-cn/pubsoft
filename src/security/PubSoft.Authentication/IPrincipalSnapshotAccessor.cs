namespace PubSoft.Authentication;

public interface IPrincipalSnapshotAccessor
{
    PrincipalSnapshot? GetCurrent();
}

public interface IAuthenticatedSubjectAccessor
{
    AuthenticatedSubject? GetCurrent();
}