using PubSoft.Domain;

namespace PubSoft.Identity;

/// <summary>Identity user aggregate: profile, credential hash, lockout state, and an opaque extension payload.</summary>
public sealed class IdentityUser : FullAuditedAggregateRoot, IIdentityUser
{
    private const int MaxUserNameLength = 256;
    private const int MaxNameLength = 256;
    private const int MaxPhoneNumberLength = 32;
    private const int MaxPasswordHashLength = 512;

    /// <summary>Creates a user with a positive Snowflake id and a login name.</summary>
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

    private IdentityUser()
    {
    }

    /// <summary>Login name.</summary>
    public string UserName { get; private set; } = string.Empty;
    /// <summary>Uppercased login name for unique lookup.</summary>
    public string NormalizedUserName { get; private set; } = string.Empty;
    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;
    /// <summary>Phone number when set.</summary>
    public string? PhoneNumber { get; private set; }
    /// <summary>Whether the phone number has been verified.</summary>
    public bool PhoneNumberConfirmed { get; private set; }
    /// <summary>Credential hash; null for external-login-only users.</summary>
    public string? PasswordHash { get; private set; }
    /// <summary>Rotated whenever credentials change - validators should treat a changed stamp as "re-authenticate".</summary>
    public string SecurityStamp { get; private set; } = CreateSecurityStamp();

    /// <summary>Changes the login name and refreshes its normalized form.</summary>
    public void SetUserName(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var normalizedUserName = userName.Trim();
        if (normalizedUserName.Length > MaxUserNameLength)
            throw new ArgumentException($"User names cannot exceed {MaxUserNameLength} characters.", nameof(userName));
        UserName = normalizedUserName;
        NormalizedUserName = UserName.ToUpperInvariant();
    }

    /// <summary>Updates display name, phone number, and its confirmation state.</summary>
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

    /// <summary>Sets a new credential hash and rotates the security stamp.</summary>
    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (passwordHash.Length > MaxPasswordHashLength)
            throw new ArgumentException($"Password hashes cannot exceed {MaxPasswordHashLength} characters.", nameof(passwordHash));
        PasswordHash = passwordHash;
        SecurityStamp = CreateSecurityStamp();
    }

    /// <summary>Removes the credential (external-login-only account) and rotates the security stamp.</summary>
    public void RemovePassword()
    {
        PasswordHash = null;
        SecurityStamp = CreateSecurityStamp();
    }

    /// <summary>Consecutive failed access attempts since the last success or reset.</summary>
    public int AccessFailedCount { get; private set; }
    /// <summary>When the current lockout expires; null when not locked out.</summary>
    public DateTimeOffset? LockoutEnd { get; private set; }

    /// <summary>Opaque host-defined extension payload (JSON). The entity validates nothing about its shape.</summary>
    public string? ExtJson { get; set; }

    /// <summary>Whether the account is locked out at <paramref name="now"/>.</summary>
    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    /// <summary>
    /// Counts a failed access attempt; reaching <paramref name="maxFailedAttempts"/> ends the
    /// streak and locks the account until <paramref name="now"/> + <paramref name="lockoutDuration"/>.
    /// </summary>
    /// <summary>Counts one failed attempt; reaching <paramref name="maxFailedAttempts"/> clears the counter and locks until <paramref name="now"/> + <paramref name="lockoutDuration"/>.</summary>
    public void RecordAccessFailure(int maxFailedAttempts, TimeSpan lockoutDuration, DateTimeOffset now)
    {
        if (maxFailedAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFailedAttempts));
        if (lockoutDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lockoutDuration));

        if (++AccessFailedCount >= maxFailedAttempts)
        {
            AccessFailedCount = 0;
            LockoutEnd = now.Add(lockoutDuration);
        }
    }

    /// <summary>Locks the account until the given instant.</summary>
    public void Lockout(DateTimeOffset until) => LockoutEnd = until;

    /// <summary>Clears the failure counter and any lockout.</summary>
    public void ResetAccessFailures()
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
    }

    private static string CreateSecurityStamp() => Guid.NewGuid().ToString("N");
}