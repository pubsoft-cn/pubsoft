using PubSoft.Core;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CoreTailWave2Tests
{
    [Fact]
    public void ResultOkAndFail()
    {
        var ok = Result<int>.Ok(42);
        Assert.True(ok.IsOk);
        Assert.Equal(42, ok.Value);
        Assert.Equal(42, ok.Unwrap(0));
        Assert.Contains("42", ok.ToString());
        var fail = Result<int>.Fail(new ResultError("E1", "bad"));
        Assert.False(fail.IsOk);
        Assert.Equal("E1", fail.Error.Code);
        Assert.Equal(0, fail.Unwrap(0));
        Assert.Throws<InvalidOperationException>(() => fail.Value);
        Assert.Contains("E1", fail.ToString());
    }

    [Fact]
    public void ResultMapAndBind()
    {
        var ok = Result<int>.Ok(2);
        Assert.Equal(4, ok.Map(x => x * 2).Value);
        Assert.Equal(8, ok.Bind(x => Result<int>.Ok(x * 4)).Value);
        var fail = Result<int>.Fail(new ResultError("E", "x"));
        Assert.False(fail.Map(x => x * 2).IsOk);
        Assert.False(fail.Bind(x => Result<int>.Ok(x)).IsOk);
    }

    [Fact]
    public void PagedResultDtoCtors()
    {
        var p = new PagedResultDto<string>(10, ["a", "b"]);
        Assert.Equal(10, p.TotalCount);
        Assert.Equal(2, p.Items.Count);
        var def = new PagedResultDto<string>();
        Assert.Empty(def.Items);
        Assert.Equal(0, def.TotalCount);
    }

    [Fact]
    public void SnowflakeAndGenderMembers()
    {
        Assert.Equal(31, SnowflakeIdGenerator.MaxWorkerId);
        Assert.True(Enum.IsDefined(Gender.Male));
        Assert.True(Enum.IsDefined(Gender.Female));
        Assert.True(Enum.IsDefined(Gender.Unknown));
    }
}
