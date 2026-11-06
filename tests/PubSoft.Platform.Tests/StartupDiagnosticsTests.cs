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
}
