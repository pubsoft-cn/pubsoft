namespace PubSoft.Security;

/// <summary>Result of a refresh-token rotation attempt.</summary>
/// <param name="Verdict">Rotation verdict.</param>
/// <param name="NewToken">Successor token when <see cref="RefreshRotationVerdict.Rotated"/>; otherwise null.</param>
/// <param name="FamilyId">Resolved family id when known; otherwise null.</param>
public sealed record RefreshRotationResult(RefreshRotationVerdict Verdict, string? NewToken, string? FamilyId);

