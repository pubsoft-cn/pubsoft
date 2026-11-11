namespace PubSoft.Security.EntityFrameworkCore;

/// <summary>EF persistence record for one refresh token inside a rotation family.</summary>
public sealed class RefreshTokenFamilyStateRecord
{
    /// <summary>Snowflake primary key; application-assigned via IIdGenerator, never store-generated.</summary>
    public long Id { get; set; }

    /// <summary>SHA-256 hex of the opaque refresh token; unique lookup column.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Family identifier shared by every rotation descendant.</summary>
    public string FamilyId { get; set; } = string.Empty;

    /// <summary>Opaque subject string the token was issued for.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>UTC issue time.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>UTC expiry of this individual token; the row may outlive it until pruned.</summary>
    public DateTimeOffset TokenExpiresAt { get; set; }

    /// <summary>Absolute UTC deadline of the whole family.</summary>
    public DateTimeOffset FamilyExpiresAt { get; set; }

    /// <summary>True once the token has been exchanged; reuse signals a leak.</summary>
    public bool Consumed { get; set; }
}
