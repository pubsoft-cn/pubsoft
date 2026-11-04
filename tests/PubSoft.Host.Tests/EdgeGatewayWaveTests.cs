using Microsoft.Extensions.DependencyInjection;
using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeGatewayWaveTests
{
    [Fact]
    public void EdgeGatewayOptionsDefaults()
    {
        var o = new EdgeGatewayOptions();
        Assert.Equal("Edge:Gateway", EdgeGatewayOptions.SectionName);
        Assert.Equal(300, o.TimestampToleranceSeconds);
        Assert.False(o.EnablePseudoPaths);
    }

    [Fact]
    public void EdgeSchemeAttributeHoldsSchemes()
    {
        var a = new EdgeSchemeAttribute(EdgeAuthScheme.Hmac, EdgeAuthScheme.Md5);
        Assert.Equal(2, a.Schemes.Length);
        Assert.Contains(EdgeAuthScheme.Hmac, a.Schemes);
        Assert.Contains(EdgeAuthScheme.Md5, a.Schemes);
    }

    [Fact]
    public void AddEdgeGatewayRegistersServices()
    {
        var s = new ServiceCollection();
        s.AddEdgeGateway();
        Assert.Contains(s, d => d.ServiceType == typeof(EdgeHandlerRegistry) || d.ServiceType.Name.Contains("Edge"));
    }
}
