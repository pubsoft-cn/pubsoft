namespace PubSoft.Security;

/// <summary>Outcome of an atomic refresh-token consume attempt.</summary>
public enum RefreshConsumeResult
{
    /// <summary>The token was flipped to consumed by this call.</summary>
    Consumed,

    /// <summary>The token was already consumed - a reuse signal.</summary>
    AlreadyConsumed,

    /// <summary>The token's family is revoked.</summary>
    FamilyRevoked
}

