namespace PubSoft.Security;

/// <summary>
/// Server-side record for one refresh token inside a rotation family. Stores only the
/// SHA-256 hash of the presented token - raw token values are never persisted.
/// </summary>
/// <param name="TokenHash">SHA-256 hex of the opaque refresh token.</param>
/// <param name="FamilyId">Family identifier shared by every rotation descendant.</param>
/// <param name="Subject">Opaque subject string the token was issued for.</param>
/// <param name="IssuedAt">UTC issue time.</param>
/// <param name="TokenExpiresAt">UTC expiry of this individual token.</param>
/// <param name="FamilyExpiresAt">Absolute UTC deadline of the whole family.</param>
/// <param name="Consumed">True once the token has been exchanged; reuse signals a leak.</param>
public sealed record RefreshTokenFamilyRecord(
    string TokenHash,
    string FamilyId,
    string Subject,
    DateTimeOffset IssuedAt,
    DateTimeOffset TokenExpiresAt,
    DateTimeOffset FamilyExpiresAt,
    bool Consumed);
