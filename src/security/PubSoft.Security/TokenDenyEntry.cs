namespace PubSoft.Security;

/// <summary>One revocation entry: a token identifier denied until its own expiry.</summary>
/// <param name="TokenId">Stable token identifier - e.g. <see cref="TokenDenyIds.ForToken"/> output or a jti.</param>
/// <param name="Subject">Optional subject for audit; bounded at 256 chars by implementations.</param>
/// <param name="ExpiresAt">Absolute UTC expiry of the denied token; the entry self-cleans past it.</param>
/// <param name="Reason">Optional operator-visible reason; bounded at 256 chars by implementations.</param>
public sealed record TokenDenyEntry(string TokenId, string? Subject, DateTimeOffset ExpiresAt, string? Reason);

