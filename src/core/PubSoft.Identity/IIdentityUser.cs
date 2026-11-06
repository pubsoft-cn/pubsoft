namespace PubSoft.Identity;

/// <summary>
/// Read-only user profile contract. It carries no credentials and implies no authentication,
/// tenant-selection, role, or authorization policy.
/// </summary>
public interface IIdentityUser
{
    /// <summary>The user identifier.</summary>
    long Id { get; }
    /// <summary>The login name.</summary>
    string UserName { get; }
    /// <summary>Display name.</summary>
    string Name { get; }
    /// <summary>Phone number when set.</summary>
    string? PhoneNumber { get; }
}