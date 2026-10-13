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

    public long Id { get; }
    public string UserName { get; }
    public string Name { get; }
    public string? PhoneNumber { get; }

    public static IdentityUserSnapshot FromUser(IIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new IdentityUserSnapshot(user.Id, user.UserName, user.Name, user.PhoneNumber);
    }
}