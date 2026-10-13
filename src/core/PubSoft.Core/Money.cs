namespace PubSoft.Core;

/// <summary>
/// Monetary value stored in minor units to avoid floating-point precision errors.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>, IComparable
{
    public long Fen { get; }

    public Money(long fen) => Fen = fen;

    public static Money FromYuan(decimal yuan) => new((long)Math.Round(yuan * 100m, MidpointRounding.AwayFromZero));
    public static Money FromFen(long fen) => new(fen);
    public decimal ToYuan() => Fen / 100m;
    public static Money Zero => new(0);
    public bool IsZero => Fen == 0;
    public bool IsPositive => Fen > 0;
    public bool IsNegative => Fen < 0;

    public static Money operator +(Money a, Money b) => new(checked(a.Fen + b.Fen));
    public static Money operator -(Money a, Money b) => new(checked(a.Fen - b.Fen));
    public static Money operator -(Money a) => new(checked(-a.Fen));
    public static Money operator *(Money a, int multiplier) => new(checked(a.Fen * multiplier));
    public static Money operator *(Money a, long multiplier) => new(checked(a.Fen * multiplier));

    public Money AllocateBps(int bps) => new((long)(Fen * (bps / 10000m)));
    public Money AllocatePercent(decimal percent) => new((long)Math.Round(Fen * (percent / 100m), MidpointRounding.AwayFromZero));

    public static bool operator >(Money a, Money b) => a.Fen > b.Fen;
    public static bool operator <(Money a, Money b) => a.Fen < b.Fen;
    public static bool operator >=(Money a, Money b) => a.Fen >= b.Fen;
    public static bool operator <=(Money a, Money b) => a.Fen <= b.Fen;
    public static bool operator ==(Money a, Money b) => a.Fen == b.Fen;
    public static bool operator !=(Money a, Money b) => a.Fen != b.Fen;

    public bool Equals(Money other) => Fen == other.Fen;
    public override bool Equals(object? obj) => obj is Money other && Equals(other);
    public override int GetHashCode() => Fen.GetHashCode();
    public int CompareTo(Money other) => Fen.CompareTo(other.Fen);
    public int CompareTo(object? obj) => obj is Money other ? CompareTo(other) : throw new ArgumentException("Incompatible type", nameof(obj));

    public static Money Max(Money a, Money b) => a >= b ? a : b;
    public static Money Min(Money a, Money b) => a <= b ? a : b;
    public override string ToString() => $"{ToYuan():F2} 元";
}
