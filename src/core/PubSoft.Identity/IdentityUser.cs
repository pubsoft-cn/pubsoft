using PubSoft.Domain;

namespace PubSoft.Identity;

public sealed class IdentityUser : FullAuditedAggregateRoot, IIdentityUser
{
    private const int MaxUserNameLength = 256;
    private const int MaxNameLength = 256;
    private const int MaxPhoneNumberLength = 32;
    private const int MaxPasswordHashLength = 512;

    public string UserName { get; private set; } = string.Empty;
    public string NormalizedUserName { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public bool PhoneNumberConfirmed { get; private set; }
    public string? PasswordHash { get; private set; }
    public string SecurityStamp { get; private set; } = CreateSecurityStamp();

    private IdentityUser()
    {
    }

    public IdentityUser(long id, string userName, long? tenantId = null, string? name = null)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "User ID must be positive.");
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId), "Tenant ID must be positive when specified.");

        Id = id;
        TenantId = tenantId;
        SetUserName(userName);
        Name = name ?? UserName;
    }

    public void SetUserName(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var normalizedUserName = userName.Trim();
        if (normalizedUserName.Length > MaxUserNameLength)
            throw new ArgumentException($"User names cannot exceed {MaxUserNameLength} characters.", nameof(userName));
        UserName = normalizedUserName;
        NormalizedUserName = UserName.ToUpperInvariant();
    }

    public void UpdateProfile(string name, string? phoneNumber, bool phoneNumberConfirmed)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Names cannot exceed {MaxNameLength} characters.", nameof(name));
        if (phoneNumber?.Length > MaxPhoneNumberLength)
            throw new ArgumentException($"Phone numbers cannot exceed {MaxPhoneNumberLength} characters.", nameof(phoneNumber));
        Name = name;
        PhoneNumber = phoneNumber;
        PhoneNumberConfirmed = phoneNumber is not null && phoneNumberConfirmed;
    }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (passwordHash.Length > MaxPasswordHashLength)
            throw new ArgumentException($"Password hashes cannot exceed {MaxPasswordHashLength} characters.", nameof(passwordHash));
        PasswordHash = passwordHash;
        SecurityStamp = CreateSecurityStamp();
    }

    public void RemovePassword()
    {
        PasswordHash = null;
        SecurityStamp = CreateSecurityStamp();
    }

    private static string CreateSecurityStamp() => Guid.NewGuid().ToString("N");
}