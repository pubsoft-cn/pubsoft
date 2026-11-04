using PubSoft.Authorization;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuthorizationAttrsWaveTests
{
    [Fact]
    public void RequirePermissionAttributeFields()
    {
        var a = new RequirePermissionAttribute("kms", PermissionFlags.Delete, "cc");
        Assert.Equal("kms", a.ModuleKey);
        Assert.Equal(PermissionFlags.Delete, a.Flags);
        Assert.Equal("cc", a.ClientCode);
        var def = new RequirePermissionAttribute("m", PermissionFlags.Query);
        Assert.Equal(string.Empty, def.ClientCode);
    }

    [Fact]
    public void RequireCustomPermissionAttributeFields()
    {
        var a = new RequireCustomPermissionAttribute("kms", "audit.view", "cc");
        Assert.Equal("kms", a.ModuleKey);
        Assert.Equal("audit.view", a.PermissionName);
        Assert.Equal("cc", a.ClientCode);
    }

    [Fact]
    public void RequireExtPermissionAttributeFields()
    {
        var a = new RequireExtPermissionAttribute("kms", 4u, "cc");
        Assert.Equal("kms", a.ModuleKey);
        Assert.Equal(4u, a.ExtFlag);
        Assert.Equal("cc", a.ClientCode);
    }

    [Fact]
    public void PermissionModuleAttributeDescription()
    {
        var a = new PermissionModuleAttribute("kms") { Description = "desc" };
        Assert.Equal("desc", a.Description);
    }

    [Fact]
    public void AutoPermissionOptionsCtor()
    {
        var o = new AutoPermissionOptions();
        Assert.NotNull(o);
    }
}
