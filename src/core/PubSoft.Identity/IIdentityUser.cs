namespace PubSoft.Identity;

/// <summary>
/// Read-only user profile contract. It carries no credentials and implies no authentication,
/// tenant-selection, role, or authorization policy.
/// </summary>
public interface IIdentityUser
{
    long Id { get; }
    string UserName { get; }
    string Name { get; }
    string? PhoneNumber { get; }
}