using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Platform.Tests;

public sealed class StartupDiagnosticsTests
{
    private sealed class FakeProvider : IStartupDiagnostics
    {
        public IEnumerable<StartupDiagnosticsEntry> Collect()
        {
            yield return new StartupDiagnosticsEntry("tokens", StartupDiagnosticsStatus.Warning, "loopback http allowed");
            yield return new StartupDiagnosticsEntry("nonce", StartupDiagnosticsStatus.Ok, "redis shared");
        }
    }

    [Fact]
    public void CollectorAggregatesAllProviders()
    {
        var services = new ServiceCollection();
        services.AddPubSoftStartupDiagnostics<FakeProvider>();
        var collector = services.BuildServiceProvider().GetRequiredService<StartupDiagnosticsCollector>();
        var entries = collector.Collect();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Area == "tokens" && e.Status == StartupDiagnosticsStatus.Warning);
    }

    [Fact]
    public void JsonReportReflectsWorstStatus()
    {
        var c = new StartupDiagnosticsCollector(new IStartupDiagnostics[] { new FakeProvider() });
        var json = c.ToJson();
        Assert.Contains("\"overall\":\"warning\"", json);
        Assert.Contains("\"area\":\"tokens\"", json);
    }

    [Fact]
    public void JsonReportEscapesControlCharacters()
    {
        var p = new NastyProvider();
        var c = new StartupDiagnosticsCollector(new IStartupDiagnostics[] { p });
        var doc = System.Text.Json.JsonDocument.Parse(c.ToJson());
        var detail = doc.RootElement.GetProperty("entries")[0].GetProperty("detail").GetString();
        Assert.Equal("tab\tcr\rnl\nbs\bq\"\\x", detail);
    }

    [Fact]
    public void HostedServiceIsolatesThrowingProviders()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStartupDiagnostics>(new ThrowingProvider());
        services.AddSingleton<IStartupDiagnostics>(new FakeProvider());
        services.AddSingleton(Microsoft.Extensions.Logging.Abstractions.NullLogger<StartupDiagnosticsHostedService>.Instance);
        var sp = services.BuildServiceProvider();
        var svc = new StartupDiagnosticsHostedService(
            sp.GetServices<IStartupDiagnostics>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StartupDiagnosticsHostedService>.Instance);
        var ex = Record.Exception(() => svc.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(ex);
    }

    private sealed class NastyProvider : IStartupDiagnostics
    {
        public IEnumerable<StartupDiagnosticsEntry> Collect()
            => [new StartupDiagnosticsEntry("nasty", StartupDiagnosticsStatus.Ok, "tab\tcr\rnl\nbs\bq\"\\x")];
    }

    private sealed class ThrowingProvider : IStartupDiagnostics
    {
        public IEnumerable<StartupDiagnosticsEntry> Collect() => throw new InvalidOperationException("boom");
    }
}
