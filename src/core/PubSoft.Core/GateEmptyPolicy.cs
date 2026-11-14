namespace PubSoft.Core;

/// <summary>
/// Behavior when an allow-list is empty. Explicit per gate - the two precedents disagree
/// (credential registries historically allowed empty lists for compatibility, while channel
/// policies treat empty as all-denied), so it must never be implicit.
/// </summary>
public enum GateEmptyPolicy
{
    /// <summary>An empty list admits every candidate (legacy compatibility).</summary>
    Allow,
    /// <summary>An empty list rejects every candidate (fail-closed default).</summary>
    Deny,
}
