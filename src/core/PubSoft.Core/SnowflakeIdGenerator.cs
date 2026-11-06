namespace PubSoft.Core;

/// <summary>
/// Generates JavaScript-safe 53-bit Snowflake identifiers.
/// Layout: 41-bit timestamp | 5-bit worker ID | 7-bit sequence.
/// </summary>
/// <remarks>
/// The generator may run the timestamp ahead of the wall clock by at most
/// <see cref="MaxBorrowMilliseconds"/> ("borrowed time"): on sequence overflow it
/// issues IDs stamped with the next millisecond instead of blocking, and it
/// tolerates clock rollback within the same window. Beyond that bound it still
/// fails closed (blocking wait for catch-up, or <see cref="InvalidOperationException"/>
/// for rollback past the bound).
/// </remarks>
public sealed class SnowflakeIdGenerator
{
    private const long Epoch = 1704067200000L;
    private const int WorkerIdBits = 5;
    private const int SequenceBits = 7;
    public const long MaxWorkerId = (1L << WorkerIdBits) - 1;
    private const long MaxSequence = (1L << SequenceBits) - 1;
    private const long MaxTimestamp = Epoch + (1L << 41) - 1;
    private const int WorkerIdShift = SequenceBits;
    private const int TimestampShift = SequenceBits + WorkerIdBits;

    /// <summary>
    /// Maximum milliseconds the generator may run ahead of the wall clock
    /// (sequence-overflow borrowing and tolerated clock rollback).
    /// </summary>
    public const int MaxBorrowMilliseconds = 512;

    private readonly long _workerId;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private long _lastTimestamp = -1L;
    private long _sequence;
    private readonly object _lock = new();

    public SnowflakeIdGenerator(long workerId) : this(workerId, TimeProvider.System)
    {
    }

    public SnowflakeIdGenerator(long workerId, TimeProvider timeProvider)
    {
        if (workerId < 0 || workerId > MaxWorkerId)
            throw new ArgumentOutOfRangeException(nameof(workerId), $"Worker ID must be between 0 and {MaxWorkerId}.");
        ArgumentNullException.ThrowIfNull(timeProvider);
        _workerId = workerId;
        _timeProvider = timeProvider;
    }

    public long NextId()
    {
        lock (_lock)
        {
            var timestamp = ReadTimestamp();
            if (timestamp < _lastTimestamp)
            {
                if (_lastTimestamp - timestamp > MaxBorrowMilliseconds)
                    throw new InvalidOperationException($"Clock moved backwards by {_lastTimestamp - timestamp} milliseconds.");
                timestamp = _lastTimestamp; // tolerate skew: keep issuing on the last timestamp
            }

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                {
                    if (_lastTimestamp == MaxTimestamp)
                        throw new InvalidOperationException("The Snowflake timestamp range is exhausted.");
                    timestamp = _lastTimestamp + 1; // borrow the next millisecond instead of spinning
                    WaitIfBorrowAhead(timestamp);
                }
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = timestamp;
            return ((timestamp - Epoch) << TimestampShift) | (_workerId << WorkerIdShift) | _sequence;
        }
    }

    private long ReadTimestamp()
    {
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (timestamp < Epoch || timestamp > MaxTimestamp)
            throw new InvalidOperationException("Current time is outside the Snowflake timestamp range.");
        return timestamp;
    }

    private void WaitIfBorrowAhead(long borrowedTimestamp)
    {
        var wallClock = ReadTimestamp();
        if (borrowedTimestamp - wallClock <= MaxBorrowMilliseconds)
            return;
        while ((wallClock = ReadTimestamp()) <= borrowedTimestamp - MaxBorrowMilliseconds)
            System.Threading.Thread.Yield(); // borrowed too far ahead — wait for the clock to catch up
    }
}
