using System.Linq.Expressions;
using PubSoft.Core;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CoreWaveTests
{
    [Fact]
    public void PagedResultDtoConstructors()
    {
        var empty = new PagedResultDto<string>();
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        var full = new PagedResultDto<string>(42, ["a", "b"]);
        Assert.Equal(42, full.TotalCount);
        Assert.Equal(2, full.Items.Count);
    }

    [Fact]
    public void ResultOkFailRoundtrip()
    {
        var ok = Result<int>.Ok(7);
        Assert.True(ok.IsOk);
        Assert.Equal(7, ok.Unwrap(0));
        var fail = Result<int>.Fail("E1", "bad");
        Assert.False(fail.IsOk);
        Assert.Equal("E1", fail.Error.Code);
        Assert.Equal("bad", fail.Error.Message);
        Assert.Equal(0, fail.Unwrap(0));
        var fail2 = Result<int>.Fail(new ResultError("E2", "worse"));
        Assert.Equal("E2", fail2.Error.Code);
    }

    [Fact]
    public void ResultErrorToString()
        => Assert.Equal("E: msg", new ResultError("E", "msg").ToString());

    [Fact]
    public void GenderEnumValues()
    {
        Assert.Equal(0, (int)Gender.Unknown);
        Assert.Equal(1, (int)Gender.Male);
        Assert.Equal(2, (int)Gender.Female);
    }

    private sealed class UserSpec : Specification<string>
    {
        public UserSpec()
        {
            AddInclude(x => x.Length);
            AddInclude("owner");
            ApplyOrderBy(x => x.Length);
            ApplyPaging(10, 20);
        }
        public UserSpec(Expression<Func<string, bool>> criteria) : base(criteria) { }
    }

    [Fact]
    public void SpecificationCollectsIncludesOrderPaging()
    {
        var spec = new UserSpec();
        Assert.Null(spec.Criteria);
        Assert.Single(spec.Includes);
        Assert.Single(spec.IncludeStrings);
        Assert.Equal("owner", spec.IncludeStrings[0]);
        Assert.NotNull(spec.OrderBy);
        Assert.True(spec.IsPagingEnabled);
        Assert.Equal(10, spec.Skip);
        Assert.Equal(20, spec.Take);
    }

    [Fact]
    public void SpecificationCriteriaCtor()
    {
        var spec = new UserSpec(x => x.Length > 3);
        Assert.NotNull(spec.Criteria);
        Assert.False(spec.IsPagingEnabled);
    }
}
