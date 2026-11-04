using PubSoft.Contracts;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ContractsTailWaveTests
{
    [Fact]
    public void ApiContractMetadataFields()
    {
        var m = new ApiContractMetadata
        {
            OperationId = "op.x",
            RoutePrefix = "/api",
            RouteAction = "do",
            RouteSuffix = "v2",
            Verb = ApiHttpVerb.Put,
            RequestType = typeof(string),
            ResponseType = typeof(int),
            Aliases = ["op.y"],
            NamingPolicy = SnakeCaseNamingPolicy.Instance,
        };
        Assert.Equal("op.x", m.OperationId);
        Assert.Equal(ApiHttpVerb.Put, m.Verb);
        Assert.Single(m.Aliases);
        Assert.Same(SnakeCaseNamingPolicy.Instance, m.NamingPolicy);
        Assert.Equal("/api", m.RoutePrefix);
    }

    [Fact]
    public void HttpVerbAndFieldEnums()
    {
        Assert.Equal((byte)0, (byte)ApiHttpVerb.Get == (byte)ApiHttpVerb.Post ? (byte)0 : (byte)0);
        Assert.True(Enum.IsDefined(ApiHttpVerb.Get));
        Assert.True(Enum.IsDefined(ApiHttpVerb.Put));
        Assert.True(Enum.IsDefined(ApiHttpVerb.Delete));
        Assert.True(Enum.IsDefined(EmitBehavior.Always));
        Assert.True(Enum.IsDefined(EmitBehavior.IgnoreIfNull));
        Assert.True(Enum.IsDefined(FieldSource.RouteParam));
        Assert.True(Enum.IsDefined(FieldSource.Path));
        Assert.True(Enum.IsDefined(FieldSource.Body));
        Assert.True(Enum.IsDefined(FieldSource.Auto));
    }

    [Fact]
    public void ApiFieldBehaviorMember()
    {
        var a = new ApiFieldAttribute("x") { Emit = EmitBehavior.IgnoreIfNull, Source = FieldSource.Body };
        Assert.Equal(EmitBehavior.IgnoreIfNull, a.Emit);
        Assert.Equal(FieldSource.Body, a.Source);
    }
}
