using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace PubSoft.Logging;

public abstract class AsyncBackbone<T> : IAsyncDisposable
{
    private readonly Channel<T> _channel;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _consumerTask;
    private readonly T[] _batchBuffer;
    private int _draining;
    private int _disposed;

    protected AsyncBackbone(string name, int capacity, int batchSize = 32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        _batchBuffer = new T[batchSize];
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _consumerTask = Task.Factory.StartNew(
            () => ConsumeLoopAsync(_cancellation.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    public int PendingCount => _channel.Reader.Count;

    public bool TryPublish(T item) => Volatile.Read(ref _draining) == 0 && _channel.Writer.TryWrite(item);

    public ValueTask PublishAsync(T item, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _draining) != 0)
            return ValueTask.FromException(new InvalidOperationException("The logger is draining and no longer accepts entries."));
        return _channel.Writer.TryWrite(item)
            ? ValueTask.CompletedTask
            : PublishWhenAvailableAsync(item, cancellationToken);
    }

    public async ValueTask DrainAsync(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        Interlocked.Exchange(ref _draining, 1);
        _channel.Writer.TryComplete();

        try
        {
            await _consumerTask.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _cancellation.Cancel();
            await _consumerTask.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await DrainAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract ValueTask ProcessBatchAsync(ReadOnlyMemory<T> batch, CancellationToken cancellationToken);

    protected virtual void OnProcessingError(Exception exception, int batchCount)
    {
    }

    private async ValueTask PublishWhenAvailableAsync(T item, CancellationToken cancellationToken)
    {
        while (await _channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Volatile.Read(ref _draining) != 0)
                throw new InvalidOperationException("The logger is draining and no longer accepts entries.");
            if (_channel.Writer.TryWrite(item))
                return;
        }

        ThrowChannelClosed();
    }

    [DoesNotReturn]
    private static void ThrowChannelClosed()
        => throw new InvalidOperationException("The logger channel is closed.");

    private async Task ConsumeLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var count = 0;
                while (count < _batchBuffer.Length && _channel.Reader.TryRead(out var item))
                    _batchBuffer[count++] = item;

                if (count == 0)
                    continue;

                try
                {
                    await ProcessBatchAsync(new ReadOnlyMemory<T>(_batchBuffer, 0, count), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    OnProcessingError(exception, count);
                }
                finally
                {
                    Array.Clear(_batchBuffer, 0, count);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}