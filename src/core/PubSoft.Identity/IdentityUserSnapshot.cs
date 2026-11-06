namespace PubSoft.Identity;

/// <summary>
/// Immutable, credential-free copy of an <see cref="IIdentityUser"/> profile.
/// </summary>
public sealed class IdentityUserSnapshot : IIdentityUser
{
    private IdentityUserSnapshot(long id, string userName, string name, string? phoneNumber)
    {
        Id = id;
        UserName = userName;
        Name = name;
        PhoneNumber = phoneNumber;
    }

    /// <inheritdoc />
    public long Id { get; }
    /// <inheritdoc />
    public string UserName { get; }
    /// <inheritdoc />
    public string Name { get; }
    /// <inheritdoc />
    public string? PhoneNumber { get; }

    /// <summary>Copies a user into an immutable snapshot.</summary>
    public static IdentityUserSnapshot FromUser(IIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new IdentityUserSnapshot(user.Id, user.UserName, user.Name, user.PhoneNumber);
    }
}