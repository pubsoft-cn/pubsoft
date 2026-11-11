namespace PubSoft.Security;

/// <summary>Result of a fresh refresh-token issue.</summary>
/// <param name="Token">Opaque refresh token for the caller.</param>
/// <param name="FamilyId">Family identifier for audit/telemetry.</param>
/// <param name="ExpiresAt">UTC expiry of the issued token.</param>
public sealed record RefreshTokenIssue(string Token, string FamilyId, DateTimeOffset ExpiresAt);

