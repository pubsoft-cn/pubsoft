namespace PubSoft.Security.EntityFrameworkCore;

/// <summary>Revocation marker for a whole refresh-token family; one row per revoked family.</summary>
public sealed class RefreshTokenFamilyRevocationRecord
{
    /// <summary>Snowflake primary key; application-assigned via IIdGenerator, never store-generated.</summary>
    public long Id { get; set; }

    /// <summary>Revoked family identifier; unique.</summary>
    public string FamilyId { get; set; } = string.Empty;

    /// <summary>UTC bound of the marker; checks ignore rows at or past this instant.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
