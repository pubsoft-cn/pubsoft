using PubSoft.Logging;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LoggingTailWaveTests
{
    private sealed class Backbone(int cap) : AsyncBackbone<int>("test", cap, 100)
    {
        public List<int> Drained { get; } = [];
        protected override ValueTask ProcessBatchAsync(ReadOnlyMemory<int> items, CancellationToken ct)
        {
            lock (Drained) Drained.AddRange(items.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task BackboneTryPublishAndDrain()
    {
        await using var b = new Backbone(8);
        Assert.True(b.TryPublish(1));
        await b.DrainAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(1, b.Drained); // pump delivered via ProcessBatchAsync
        Assert.False(b.TryPublish(2)); // after drain — rejects
    }

    [Fact]
    public void AppVersionStatics()
    {
        Assert.True(AppVersion.BuildTime is null || AppVersion.BuildTime.Length > 0); // string? — null without embedded metadata
        Assert.True(AppVersion.BuildDate is null || AppVersion.BuildDate.Length == 8);
    }
}
