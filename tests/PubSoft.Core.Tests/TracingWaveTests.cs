using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Tracing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class TracingWaveTests
{
    private sealed class CollectingOutput
    {
        public List<byte[]> Batches { get; } = [];
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            Batches.Add(bytes.ToArray());
            return ValueTask.CompletedTask;
        }
        public string Text() => System.Text.Encoding.UTF8.GetString(Batches.SelectMany(b => b).ToArray());
    }

    [Fact]
    public async Task ExporterWritesSpanDocWithJoinKeys()
    {
        var output = new CollectingOutput();
        var exporter = new PubSoftSpanExporter(new PubSoftTracingOptions
        {
            ServiceName = "svc-x",
            ServiceVersion = "1.2.3",
            Output = output.WriteAsync
        });

        string expectedTraceId, expectedParentSpanId;
        using (var source = new ActivitySource("test-src"))
        {
            using var parent = source.StartActivity("parent-op", ActivityKind.Server);
            Assert.NotNull(parent);
            using var child = source.StartActivity("child-op", ActivityKind.Internal);
            Assert.NotNull(child);
            expectedTraceId = child!.TraceId.ToHexString();
            expectedParentSpanId = child.ParentSpanId.ToHexString();
            child.Stop();
            parent!.Stop();
        }
        await exporter.DisposeAsync(); // drain flushes the queued spans

        var lines = output.Text().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2, $"expected >= 2 span docs, got {lines.Length}");
        var childDoc = JsonDocument.Parse(lines.First(l => l.Contains("child-op")));
        Assert.Equal(expectedTraceId, childDoc.RootElement.GetProperty("tid").GetString());
        Assert.Equal(expectedParentSpanId, childDoc.RootElement.GetProperty("psid").GetString());
        Assert.Equal("svc-x", childDoc.RootElement.GetProperty("svc").GetString());
        Assert.Equal("1.2.3", childDoc.RootElement.GetProperty("svc_ver").GetString());
        Assert.Equal("ok", childDoc.RootElement.GetProperty("status").GetString());
        Assert.Equal("internal", childDoc.RootElement.GetProperty("kind").GetString());
        Assert.True(childDoc.RootElement.TryGetProperty("durMs", out _));
        Assert.Equal(JsonValueKind.Number, childDoc.RootElement.GetProperty("ts").ValueKind);
    }

    [Fact]
    public async Task DocOutputReceivesStructuredDocs()
    {
        var docs = new List<SpanDoc>();
        await using var exporter = new PubSoftSpanExporter(new PubSoftTracingOptions
        {
            ServiceName = "svc-doc",
            DocOutput = (batch, _) =>
            {
                docs.AddRange(batch.ToArray());
                return ValueTask.CompletedTask;
            }
        });

        using (var source = new ActivitySource("test-src-doc"))
        {
            using var activity = source.StartActivity("http-op", ActivityKind.Server);
            Assert.NotNull(activity);
            activity!.SetTag("http.request.method", "POST");
            activity.SetTag("url.path", "/api/x");
            activity.Stop();
        }
        await exporter.DisposeAsync();

        var doc = Assert.Single(docs);
        Assert.Equal("svc-doc", doc.ServiceName);
        Assert.Equal("server", doc.Kind);
        Assert.Equal("ok", doc.Status);
        Assert.Equal("HTTP POST /api/x", doc.Name);
        Assert.Equal(32, doc.TraceId.Length);
        Assert.Equal(16, doc.SpanId.Length);
    }

    [Fact]
    public void DocOutputIsMutuallyExclusiveWithOtherSinks()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PubSoftSpanExporter(
            new PubSoftTracingOptions
            {
                ServiceName = "x",
                DocOutput = (_, _) => ValueTask.CompletedTask,
                Output = (_, _) => ValueTask.CompletedTask
            }));
    }

    [Fact]
    public async Task ErrorStatusAndTagsAreEmitted()
    {
        var output = new CollectingOutput();
        var exporter = new PubSoftSpanExporter(new PubSoftTracingOptions
        {
            ServiceName = "svc-x",
            Output = output.WriteAsync
        });

        using (var source = new ActivitySource("test-src-err"))
        {
            using var activity = source.StartActivity("failing-op", ActivityKind.Server);
            activity!.SetTag("http.method", "POST");
            activity.SetStatus(ActivityStatusCode.Error, "boom");
            activity.Stop();
        }
        await exporter.DisposeAsync();

        var doc = JsonDocument.Parse(output.Text().Split('\n', StringSplitOptions.RemoveEmptyEntries).Single());
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("boom", doc.RootElement.GetProperty("err").GetString());
        Assert.Equal("POST", doc.RootElement.GetProperty("tags").GetProperty("http.method").GetString());
    }

    [Fact]
    public void OptionsValidateServiceNameAndExactlyOneOutput()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PubSoftSpanExporter(
            new PubSoftTracingOptions { Output = (_, _) => ValueTask.CompletedTask }));
        Assert.ThrowsAny<ArgumentException>(() => new PubSoftSpanExporter(
            new PubSoftTracingOptions { ServiceName = "x" }));
        Assert.ThrowsAny<ArgumentException>(() => new PubSoftSpanExporter(
            new PubSoftTracingOptions
            {
                ServiceName = "x",
                Directory = "/tmp/x",
                Output = (_, _) => ValueTask.CompletedTask
            }));
    }

    [Fact]
    public async Task FileOutputWritesSpansFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pubsoft-spans-{Guid.NewGuid():N}");
        var write = TracingFileOutput.ForDirectory(dir, "svc-file");
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"tid\":\"x\"}\n");
        await write(bytes, CancellationToken.None);

        var file = Assert.Single(Directory.GetFiles(dir, "spans-svc-file-*.jsonl"));
        Assert.Equal("{\"tid\":\"x\"}", (await File.ReadAllTextAsync(file)).Trim());
    }

    [Fact]
    public async Task DiRegistrationResolvesAndListens()
    {
        var output = new CollectingOutput();
        var services = new ServiceCollection();
        services.AddPubSoftTracing(o =>
        {
            o.ServiceName = "svc-di";
            o.Output = output.WriteAsync;
        });
        await using var provider = services.BuildServiceProvider();
        var exporter = provider.GetRequiredService<PubSoftSpanExporter>();
        Assert.NotNull(exporter);

        using (var source = new ActivitySource("test-src-di"))
        {
            using var activity = source.StartActivity("di-op", ActivityKind.Internal);
            activity!.Stop();
        }
        await exporter.DisposeAsync();
        Assert.Contains("di-op", output.Text());
    }
}
