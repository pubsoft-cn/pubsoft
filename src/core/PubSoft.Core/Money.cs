namespace PubSoft.Core;

/// <summary>
/// Monetary value stored in minor units to avoid floating-point precision errors.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>, IComparable
{

    /// <summary>Creates a value expressed in minor units (fen/cents).</summary>
    public Money(long fen) => Fen = fen;
    /// <summary>Amount in minor units.</summary>
    public long Fen { get; }
    /// <summary>A zero amount.</summary>
    public static Money Zero => new(0);
    /// <summary>Whether the amount is zero.</summary>
    public bool IsZero => Fen == 0;
    /// <summary>Whether the amount is strictly positive.</summary>
    public bool IsPositive => Fen > 0;
    /// <summary>Whether the amount is strictly negative.</summary>
    public bool IsNegative => Fen < 0;

    /// <summary>Converts a major-unit amount (yuan) to minor units, rounding half away from zero.</summary>
    public static Money FromYuan(decimal yuan) => new((long)Math.Round(yuan * 100m, MidpointRounding.AwayFromZero));
    /// <summary>Wraps a minor-unit amount.</summary>
    public static Money FromFen(long fen) => new(fen);
    /// <summary>Returns the amount in major units.</summary>
    public decimal ToYuan() => Fen / 100m;

    /// <summary>Adds two amounts, throwing on overflow.</summary>
    public static Money operator +(Money a, Money b) => new(checked(a.Fen + b.Fen));
    /// <summary>Subtracts one amount from another, throwing on overflow.</summary>
    public static Money operator -(Money a, Money b) => new(checked(a.Fen - b.Fen));
    /// <summary>Negates the amount, throwing on overflow.</summary>
    public static Money operator -(Money a) => new(checked(-a.Fen));
    /// <summary>Scales the amount by an integer multiplier.</summary>
    public static Money operator *(Money a, int multiplier) => new(checked(a.Fen * multiplier));
    /// <summary>Scales the amount by a long multiplier.</summary>
    public static Money operator *(Money a, long multiplier) => new(checked(a.Fen * multiplier));

    /// <summary>Returns the basis-point share of this amount (bps/10000), truncated.</summary>
    public Money AllocateBps(int bps) => new((long)(Fen * (bps / 10000m)));
    /// <summary>Returns the percentage share of this amount, rounding half away from zero.</summary>
    public Money AllocatePercent(decimal percent) => new((long)Math.Round(Fen * (percent / 100m), MidpointRounding.AwayFromZero));

    /// <summary>Whether <paramref name="a"/> exceeds <paramref name="b"/>.</summary>
    public static bool operator >(Money a, Money b) => a.Fen > b.Fen;
    /// <summary>Whether <paramref name="a"/> is below <paramref name="b"/>.</summary>
    public static bool operator <(Money a, Money b) => a.Fen < b.Fen;
    /// <summary>Whether <paramref name="a"/> is at least <paramref name="b"/>.</summary>
    public static bool operator >=(Money a, Money b) => a.Fen >= b.Fen;
    /// <summary>Whether <paramref name="a"/> is at most <paramref name="b"/>.</summary>
    public static bool operator <=(Money a, Money b) => a.Fen <= b.Fen;
    /// <summary>Whether two amounts are equal.</summary>
    public static bool operator ==(Money a, Money b) => a.Fen == b.Fen;
    /// <summary>Whether two amounts differ.</summary>
    public static bool operator !=(Money a, Money b) => a.Fen != b.Fen;

    /// <summary>Whether this amount equals <paramref name="other"/>.</summary>
    public bool Equals(Money other) => Fen == other.Fen;
    public override bool Equals(object? obj) => obj is Money other && Equals(other);
    public override int GetHashCode() => Fen.GetHashCode();
    /// <summary>Compares this amount with <paramref name="other"/>.</summary>
    public int CompareTo(Money other) => Fen.CompareTo(other.Fen);
    /// <summary>Compares this amount with another <see cref="Money"/> boxed as object.</summary>
    public int CompareTo(object? obj) => obj is Money other ? CompareTo(other) : throw new ArgumentException("Incompatible type", nameof(obj));

    /// <summary>Returns the larger of two amounts.</summary>
    public static Money Max(Money a, Money b) => a >= b ? a : b;
    /// <summary>Returns the smaller of two amounts.</summary>
    public static Money Min(Money a, Money b) => a <= b ? a : b;
    public override string ToString() => $"{ToYuan():F2} 元";
}
