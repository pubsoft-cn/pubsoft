using System.Diagnostics;
using PubSoft.Tracing;

var captured = 0;
await using (var exporter = new PubSoftSpanExporter(new PubSoftTracingOptions
             {
                 ServiceName = "smoke",
                 Output = (_, _) => { Interlocked.Increment(ref captured); return ValueTask.CompletedTask; }
             }))
{
    using var activity = new Activity("smoke-op");
    activity.Start();
    activity.Stop();
}
if (captured != 1)
    throw new InvalidOperationException($"Expected 1 exported span, got {captured}.");
await using var fileExporter = new PubSoftSpanExporter(new PubSoftTracingOptions
{
    ServiceName = "smoke",
    Directory = Path.Combine(Path.GetTempPath(), $"pubsoft-smoke-{Guid.NewGuid():N}")
});

Console.WriteLine("Isolated PubSoft.Tracing consumer smoke check passed.");
