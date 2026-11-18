using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.DependencyInjection;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class StartupValidationTests
{
    [Fact]
    public void GetNonEmptyReturnsValueWhenConfigured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SecretKey"] = "abc" })
            .Build();
        Assert.Equal("abc", config.GetNonEmpty("Jwt:SecretKey"));
    }

    [Fact]
    public void GetNonEmptyTreatsEmptyAndWhitespaceAsMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["empty"] = string.Empty,
                ["blank"] = "   ",
            })
            .Build();
        Assert.Null(config.GetNonEmpty("empty"));
        Assert.Null(config.GetNonEmpty("blank"));
        Assert.Null(config.GetNonEmpty("absent"));
    }

    [Fact]
    public async Task FailModeThrowsWhenCheckFails()
    {
        var services = new ServiceCollection();
        services.AddPubSoftStartupCheck("missing-dep", sp => _ = sp.GetRequiredService<IHostEnvironment>());
        services.AddPubSoftStartupValidation();
        var provider = services.BuildServiceProvider();
        var validator = Assert.IsType<StartupValidationHostedService>(
            provider.GetServices<IHostedService>().Single());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.StartAsync(CancellationToken.None));
        Assert.Contains("missing-dep", ex.Message);
    }

    [Fact]
    public async Task FailModeAggregatesEveryFailedCheck()
    {
        var services = new ServiceCollection();
        services.AddPubSoftStartupCheck("first", _ => throw new InvalidOperationException("one"));
        services.AddPubSoftStartupCheck("second", _ => throw new InvalidOperationException("two"));
        services.AddPubSoftStartupValidation();
        var provider = services.BuildServiceProvider();
        var validator = Assert.IsType<StartupValidationHostedService>(
            provider.GetServices<IHostedService>().Single());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.StartAsync(CancellationToken.None));
        Assert.Contains("one", ex.Message);
        Assert.Contains("two", ex.Message);
    }

    [Fact]
    public async Task WarnModeLogsAndContinues()
    {
        var services = new ServiceCollection();
        services.AddPubSoftStartupCheck("missing-dep", sp => _ = sp.GetRequiredService<IHostEnvironment>());
        services.AddPubSoftStartupValidation(o => o.FailureMode = StartupValidationFailureMode.Warn);
        var provider = services.BuildServiceProvider();
        var validator = Assert.IsType<StartupValidationHostedService>(
            provider.GetServices<IHostedService>().Single());
        await validator.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PassingChecksLeaveHostRunning()
    {
        var services = new ServiceCollection();
        services.AddPubSoftStartupCheck("present-dep", sp => _ = sp.GetRequiredService<IServiceProviderIsService>());
        services.AddPubSoftStartupValidation();
        var provider = services.BuildServiceProvider();
        var validator = Assert.IsType<StartupValidationHostedService>(
            provider.GetServices<IHostedService>().Single());
        await validator.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ChecksRunInsideScopeSoScopedDepsResolve()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedProbe>();
        services.AddPubSoftStartupCheck("scoped-dep", sp => _ = sp.GetRequiredService<ScopedProbe>());
        services.AddPubSoftStartupValidation();
        var provider = services.BuildServiceProvider(validateScopes: true);
        var validator = Assert.IsType<StartupValidationHostedService>(
            provider.GetServices<IHostedService>().Single());
        await validator.StartAsync(CancellationToken.None);
    }

    private sealed class ScopedProbe;
}
