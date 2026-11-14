namespace PubSoft.Core;

/// <summary>
/// Outcome of one allow-list gate in a serial pipeline: admit, or reject with a reason for
/// logs/audit. Gates evaluate in order; the first denial wins.
/// </summary>
/// <param name="Allowed">Whether the candidate passed the gate.</param>
/// <param name="Reason">Denial reason - diagnostic text, not shown to end users.</param>
public sealed record GateResult(bool Allowed, string? Reason = null)
{
    /// <summary>Admits the candidate.</summary>
    public static GateResult Allow() => new(true);

    /// <summary>Rejects the candidate with a diagnostic reason.</summary>
    public static GateResult Deny(string reason) => new(false, reason);
}
