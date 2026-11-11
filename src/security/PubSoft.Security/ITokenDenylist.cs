namespace PubSoft.Security;

/// <summary>
/// Revocation denylist - the security source of truth for "this token must no longer validate".
/// Entries live only until the denied token's own expiry; consumers check by stable token id.
/// </summary>
public interface ITokenDenylist
{
    /// <summary>Records a token id as denied until <see cref="TokenDenyEntry.ExpiresAt"/>.</summary>
    /// <param name="entry">Entry to record; already-expired entries are a no-op.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask DenyAsync(TokenDenyEntry entry, CancellationToken cancellationToken = default);

    /// <summary>True while a live (unexpired) deny entry exists for the id.</summary>
    /// <param name="tokenId">Token identifier to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<bool> IsDeniedAsync(string tokenId, CancellationToken cancellationToken = default);
}
