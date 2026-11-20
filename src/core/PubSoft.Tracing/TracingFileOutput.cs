namespace PubSoft.Tracing;

/// <summary>Built-in jsonl file output for <see cref="PubSoftSpanExporter"/> -
/// appends to <c>spans-{service}-yyyyMMdd.jsonl</c> with UTC-day rollover.</summary>
public static class TracingFileOutput
{
    /// <summary>Creates the output delegate writing <c>spans-{serviceName}-{yyyyMMdd}.jsonl</c>
    /// under <paramref name="directory"/> (created when missing; rolls at UTC midnight).</summary>
    public static Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> ForDirectory(
        string directory, string serviceName, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (serviceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Service name must be file-name safe.", nameof(serviceName));

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new SemaphoreSlim(1, 1);
        var dir = Path.GetFullPath(directory);
        FileStream? stream = null;
        var day = DateOnly.MinValue;

        return async (bytes, cancellationToken) =>
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (stream is null || today != day)
                {
                    if (stream is not null)
                        await stream.DisposeAsync().ConfigureAwait(false);
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, $"spans-{serviceName}-{today:yyyy-MM-dd}.jsonl");
                    stream = new FileStream(
                        path, FileMode.Append, FileAccess.Write, FileShare.Read,
                        bufferSize: 1 << 16, FileOptions.Asynchronous);
                    day = today;
                }
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        };
    }
}
