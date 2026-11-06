namespace PubSoft.Authentication;

/// <summary>Returns the ambient claims snapshot.</summary>
public interface IPrincipalSnapshotAccessor
{
    /// <summary>Current snapshot; null when no authenticated context.</summary>
    PrincipalSnapshot? GetCurrent();
}

/// <summary>Returns the ambient authenticated subject.</summary>
public interface IAuthenticatedSubjectAccessor
{
    /// <summary>Current subject; null when anonymous.</summary>
    AuthenticatedSubject? GetCurrent();
}

/// <summary>Returns the ambient resolved subject.</summary>
public interface ICurrentSubjectAccessor
{
    /// <summary>Current resolved subject; null when anonymous.</summary>
    CurrentSubject? GetCurrent();
}