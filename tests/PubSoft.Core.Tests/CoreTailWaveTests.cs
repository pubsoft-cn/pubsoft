using PubSoft.Core;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CoreTailWaveTests
{
    [Fact]
    public void MoneyValueSemantics()
    {
        var m = Money.FromYuan(12.34m);
        Assert.Equal(1234L, m.Fen);
        Assert.Equal(12.34m, m.ToYuan());
        Assert.True(m.IsPositive);
        Assert.False(m.IsZero);
        Assert.True(Money.Zero.IsZero);
    }

    [Fact]
    public void MoneyArithmeticAndComparison()
    {
        var a = Money.FromFen(100);
        var b = Money.FromFen(200);
        Assert.Equal(300, (a + b).Fen);
        Assert.Equal(100, (b - a).Fen);
        Assert.Equal(200, (a * 2).Fen);
        Assert.Equal(-100, (-a).Fen);
        Assert.True(b > a);
        Assert.Equal(b, Money.Max(a, b));
        Assert.Equal(a, b.CompareTo(a) > 0 ? a : b);
        Assert.Equal(a, Money.FromFen(100));
        Assert.Equal(a.GetHashCode(), Money.FromFen(100).GetHashCode());
    }

    [Fact]
    public void MoneyAllocation()
    {
        var m = Money.FromFen(10000);
        Assert.Equal(2500, m.AllocateBps(2500).Fen);   // 25%
        Assert.Equal(3333, m.AllocatePercent(33.33m).Fen);
        Assert.Contains("元", m.ToString());
    }

    private enum Tiny { A = 1, B = 2 }
    [Flags] private enum F { X = 1, Y = 2 }

    [Fact]
    public void GuardValidators()
    {
        Assert.Equal("v", Guard.NotNull("v"));
        Assert.Throws<DomainException>(() => Guard.NotNull(null));
        Assert.Throws<DomainException>(() => Guard.NotEmpty(" "));
        Assert.Throws<DomainException>(() => Guard.MaxLength("toolong", 3));
        Assert.Throws<DomainException>(() => Guard.Phone("123abc"));
        Assert.Equal("13800138000", Guard.Phone("13800138000"));
        Assert.Equal(Tiny.A, Guard.EnumDefined(Tiny.A));
        Assert.Throws<DomainException>(() => Guard.EnumDefined((Tiny)99));
        Assert.Equal(F.X | F.Y, Guard.FlagsDefined(F.X | F.Y));
        Assert.Throws<DomainException>(() => Guard.FlagsDefined((F)8));
    }

    [Fact]
    public void ApiResponseFactories()
    {
        var ok = ApiResponse<string>.Success("data");
        Assert.Equal(0, ok.Code);
        Assert.Equal("data", ok.Data);
        var fail = ApiResponse<string>.Fail(500, "err");
        Assert.Equal(500, fail.Code);
        Assert.Equal("err", fail.Message);
        var plain = ApiResponse.Success();
        Assert.Equal(0, plain.Code);
    }
}
