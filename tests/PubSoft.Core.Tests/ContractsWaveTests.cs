using PubSoft.Contracts;
using CGuard = PubSoft.Contracts.Guard;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ContractsWaveTests
{
    [Fact]
    public void GuardRequiredAndAny()
    {
        Assert.Null(CGuard.Required("f", "v"));
        Assert.Equal("f required.", CGuard.Required("f", null));
        Assert.Equal("f required.", CGuard.Required("f", "  "));
        Assert.Null(CGuard.RequiredAny("f", null, "v"));
        Assert.Equal("f required.", CGuard.RequiredAny("f", null, " "));
    }

    [Fact]
    public void GuardPositiveAndAmount()
    {
        Assert.Null(CGuard.Positive("a", 1m));
        Assert.Equal("a must be positive.", CGuard.Positive("a", 0m));
        Assert.Equal("a must be positive.", CGuard.Positive("a", -1L));
        Assert.Null(CGuard.RequiredAmount("a", "10.5"));
        Assert.Equal("a required.", CGuard.RequiredAmount("a", null));
        Assert.Equal("a must be a positive amount.", CGuard.RequiredAmount("a", "abc"));
        Assert.Equal("a must be a positive amount.", CGuard.RequiredAmount("a", "-3"));
    }

    [Fact]
    public void GuardIsValidAndTryValidate()
    {
        Assert.True(CGuard.IsValid((string?)null, (string?)null));
        Assert.False(CGuard.IsValid(null, "err"));
        // params-array edge: single null binds the array itself → NRE inside TryValidate
        Assert.Throws<ArgumentNullException>(() => CGuard.TryValidate(out _, null));
        Assert.False(CGuard.TryValidate(out var e1, "ok?")); // non-empty string = error present → invalid
        Assert.Equal("ok?", e1);
        Assert.False(CGuard.TryValidate(out var e2, "bad", null)); // first non-empty wins
        Assert.Equal("bad", e2);
        Assert.True(CGuard.TryValidate(out var e3, (string?)null)); // all null → valid
        Assert.Null(e3);
    }

    [Fact]
    public void NamingPoliciesConvert()
    {
        Assert.Equal("orderId", CamelCaseNamingPolicy.Instance.ConvertName("OrderId"));
        Assert.Equal("order_id", SnakeCaseNamingPolicy.Instance.ConvertName("OrderId"));
        Assert.Equal("OrderId", PascalCaseNamingPolicy.Instance.ConvertName("OrderId"));
    }

    [Fact]
    public void ApiOperationAttributeDefaults()
    {
        var a = new ApiOperationAttribute("op1");
        Assert.Equal("op1", a.OperationId);
        Assert.Empty(a.Aliases);
        Assert.Equal(ApiHttpVerb.Post, a.Verb);
    }

    [Fact]
    public void ApiFieldAttributeDefaults()
    {
        var f = new ApiFieldAttribute("n");
        Assert.Equal("n", f.Name);
        Assert.Equal(0, f.Order);
        Assert.Equal(EmitBehavior.Always, f.Emit);
        Assert.Equal(FieldSource.Auto, f.Source);
        Assert.False(f.IsEncrypted);
        Assert.False(f.IsRequired);
        Assert.False(f.Flatten);
    }
}
