using System.Text.Json;
using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Security.Tests;

/// <summary>C-layer: OAuth provider result records round-trip JSON cleanly.</summary>
public sealed class OAuthResultWaveTests
{
    [Fact]
    public void WeChatAccessTokenResultRoundtrips()
    {
        var r = new WeChatAccessTokenResult { Success = true, AccessToken = "tk", Message = null };
        var back = JsonSerializer.Deserialize<WeChatAccessTokenResult>(JsonSerializer.Serialize(r));
        Assert.NotNull(back);
        Assert.True(back.Success);
        Assert.Equal("tk", back.AccessToken);
    }

    [Fact]
    public void WeChatResultDefaultsAreSafe()
    {
        var r = JsonSerializer.Deserialize<WeChatAccessTokenResult>("{}");
        Assert.NotNull(r);
        Assert.False(r.Success);
        Assert.NotNull(r.AccessToken); // default "" not null
    }

    [Fact]
    public void OAuthPhoneResultRoundtrips()
    {
        var r = new OAuthPhoneResult { Success = true, PhoneNumber = "138", PurePhoneNumber = "138" };
        var back = JsonSerializer.Deserialize<OAuthPhoneResult>(JsonSerializer.Serialize(r));
        Assert.NotNull(back);
        Assert.Equal("138", back.PhoneNumber);
    }
}
