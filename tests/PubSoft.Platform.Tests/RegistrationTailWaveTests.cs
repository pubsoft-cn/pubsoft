using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Events;
using PubSoft.Notifications;
using PubSoft.Tokens;
using Xunit;

namespace PubSoft.Platform.Tests;

/// <summary>B-layer tail: Tokens/Events/Notifications registration smoke.</summary>
public sealed class RegistrationTailWaveTests
{
    [Fact]
    public void TokensRegister()
    {
        var services = new ServiceCollection().AddPubSoftTokens();
        Assert.NotEmpty(services);
        Assert.Contains(services, d => d.ServiceType.FullName!.Contains("Token"));
    }

    [Fact]
    public void DomainEventsRegister()
    {
        var services = new ServiceCollection().AddPubSoftEvents();
        Assert.NotEmpty(services);
    }

    [Fact]
    public void NotificationChannelsRegisterFromSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:Channels:0:Type"] = "noop" })
            .Build();
        var services = new ServiceCollection();
        services.AddPubSoftNotificationChannels(config.GetSection("Notifications"));
        Assert.NotEmpty(services);
    }
}
