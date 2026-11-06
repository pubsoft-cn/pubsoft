namespace PubSoft.Core;

/// <summary>
/// Declares that a member's per-call cost is bounded — once-per-lifetime (startup/registration),
/// periodic (timer), input-bounded, or capacity-triggered — so PUBSOFT003 and future
/// hot-path analyzers skip it. Follows the BCL "Requires" family shape: the
/// <see cref="Reason"/> is a mandatory constructor argument so the exemption documents itself.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BoundedCostAttribute : Attribute
{
    public BoundedCostAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Bounded-cost declarations must state what bounds the work.", nameof(reason));
        Reason = reason;
    }

    public string Reason { get; }
}
