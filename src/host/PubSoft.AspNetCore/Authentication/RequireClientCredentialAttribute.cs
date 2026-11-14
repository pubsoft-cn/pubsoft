namespace PubSoft.AspNetCore.Authentication;


/// <summary>
/// Marks an endpoint as accepting machine credentials via <c>X-Client-Id</c>/<c>X-Client-Secret</c>
/// headers. By default only a valid machine credential passes; set <see cref="AllowAuthenticatedUser"/>
/// to also let an already-authenticated principal through.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireClientCredentialAttribute : Attribute
{
    /// <summary>Whether an already-authenticated user principal bypasses the credential check.</summary>
    public bool AllowAuthenticatedUser { get; set; }
}
