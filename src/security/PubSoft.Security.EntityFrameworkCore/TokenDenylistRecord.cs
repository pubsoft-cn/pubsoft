namespace PubSoft.Security.EntityFrameworkCore;

/// <summary>EF persistence record for one denied token id.</summary>
public sealed class TokenDenylistRecord
{
    /// <summary>Snowflake primary key; application-assigned via IIdGenerator, never store-generated.</summary>
    public long Id { get; set; }

    /// <summary>Denied token identifier (hash or jti); unique.</summary>
    public string TokenId { get; set; } = string.Empty;

    /// <summary>Optional subject for audit.</summary>
    public string? Subject { get; set; }

    /// <summary>Optional operator-visible reason.</summary>
    public string? Reason { get; set; }

    /// <summary>UTC time the entry was recorded.</summary>
    public DateTimeOffset DeniedAt { get; set; }

    /// <summary>UTC expiry of the denied token; the entry stops matching past this instant.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
