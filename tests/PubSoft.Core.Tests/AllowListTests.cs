using System.Net;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AllowListTests
{
    [Fact]
    public void IpAllowListEmptyRespectsPolicy()
    {
        Assert.False(new IpAllowList(null).IsAllowed("10.0.0.1"));
        Assert.False(new IpAllowList([]).IsAllowed(IPAddress.Parse("10.0.0.1")));
        Assert.True(new IpAllowList(null, GateEmptyPolicy.Allow).IsAllowed("10.0.0.1"));
        Assert.True(new IpAllowList([], GateEmptyPolicy.Allow).IsAllowed((string?)null));
        // Even under allow-empty, a populated list still denies non-members.
        Assert.False(new IpAllowList(["1.2.3.4"], GateEmptyPolicy.Allow).IsAllowed("9.9.9.9"));
    }

    [Fact]
    public void IpAllowListMatchesSinglesAndCidr()
    {
        var list = new IpAllowList(["10.0.0.5", "192.168.0.0/16", "::1"]);
        Assert.True(list.IsAllowed("10.0.0.5"));
        Assert.True(list.IsAllowed("192.168.4.77"));
        Assert.True(list.IsAllowed(IPAddress.Parse("::1")));
        Assert.False(list.IsAllowed("10.0.0.6"));
        Assert.False(list.IsAllowed("192.167.1.1"));
        Assert.False(list.IsAllowed((string?)null));
        Assert.False(list.IsAllowed("not-an-ip"));
        Assert.False(list.IsAllowed((IPAddress?)null));
    }

    [Fact]
    public void IpAllowListNormalizesMappedV6()
    {
        var list = new IpAllowList(["10.0.0.5"]);
        Assert.True(list.IsAllowed(IPAddress.Parse("::ffff:10.0.0.5")));
    }

    [Fact]
    public void IpAllowListRejectsMalformedEntries()
    {
        Assert.Throws<ArgumentException>(() => new IpAllowList(["bogus"]));
        Assert.Throws<ArgumentException>(() => new IpAllowList(["10.0.0.0/x"]));
        Assert.Throws<ArgumentException>(() => new IpAllowList(["10.0.0.0/33"]));
    }

    [Fact]
    public void JsonAllowListEmptyRespectsPolicy()
    {
        Assert.False(new JsonAllowList(null).IsAllowed("app-1"));
        Assert.False(new JsonAllowList("[]").IsAllowed("app-1"));
        Assert.True(new JsonAllowList(null, GateEmptyPolicy.Allow).IsAllowed("anything"));
        Assert.False(new JsonAllowList("[\"app-1\"]", GateEmptyPolicy.Allow).IsAllowed("app-2"));
    }

    [Fact]
    public void JsonAllowListMatchesOrdinal()
    {
        var list = new JsonAllowList("""["app-1","svc-2"]""");
        Assert.True(list.IsAllowed("app-1"));
        Assert.True(list.IsAllowed("svc-2"));
        Assert.False(list.IsAllowed("APP-1"));
        Assert.False(list.IsAllowed("app-1 "));
        Assert.False(list.IsAllowed(null));
    }

    [Fact]
    public void JsonAllowListRejectsMalformedColumns()
    {
        Assert.Throws<ArgumentException>(() => new JsonAllowList("not-json"));
        Assert.Throws<ArgumentException>(() => new JsonAllowList("{\"a\":1}"));
        Assert.Throws<ArgumentException>(() => new JsonAllowList("[1,2]"));
        Assert.Throws<ArgumentException>(() => new JsonAllowList("[\"\"]"));
    }

    [Fact]
    public void GateResultFactories()
    {
        Assert.True(GateResult.Allow().Allowed);
        var denied = GateResult.Deny("not in allow-list");
        Assert.False(denied.Allowed);
        Assert.Equal("not in allow-list", denied.Reason);
    }
}
