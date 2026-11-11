namespace PubSoft.Security;

/// <summary>Verdict of a refresh-token rotation attempt.</summary>
public enum RefreshRotationVerdict
{
    /// <summary>Token consumed; a successor was issued.</summary>
    Rotated,

    /// <summary>An already-consumed token was presented; the whole family was revoked.</summary>
    ReuseDetected,

    /// <summary>No record exists for the presented token.</summary>
    Unknown,

    /// <summary>The token or its family deadline has passed.</summary>
    Expired,

    /// <summary>The token's family is revoked.</summary>
    FamilyRevoked
}

