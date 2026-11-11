namespace PubSoft.Security;

/// <summary>
/// Persistence contract for refresh-token rotation families. Implementations must make
/// <see cref="TryConsumeAsync"/> atomic - a lost race there is the whole security model.
/// </summary>
public interface IRefreshTokenFamilyStore
{
    /// <summary>Reads a token record by its SHA-256 hash; null when unknown or evicted.</summary>
    /// <param name="tokenHash">SHA-256 hex of the presented token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<RefreshTokenFamilyRecord?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Persists a freshly issued token record.</summary>
    /// <param name="record">Record to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SaveAsync(RefreshTokenFamilyRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically flips a token to consumed. Implementations must check the family-revoked
    /// marker inside the same atomic step so revocation always wins the race.
    /// </summary>
    /// <param name="tokenHash">SHA-256 hex of the presented token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<RefreshConsumeResult> TryConsumeAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Marks a family revoked until the given absolute deadline (marker lives no longer).</summary>
    /// <param name="familyId">Family to revoke.</param>
    /// <param name="until">Absolute UTC bound for the revocation marker.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RevokeFamilyAsync(string familyId, DateTimeOffset until, CancellationToken cancellationToken = default);

    /// <summary>Revokes every known family of a subject; returns how many were revoked.</summary>
    /// <param name="subject">Subject whose families should be revoked.</param>
    /// <param name="until">Absolute UTC bound for the revocation markers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<int> RevokeSubjectAsync(string subject, DateTimeOffset until, CancellationToken cancellationToken = default);
}
