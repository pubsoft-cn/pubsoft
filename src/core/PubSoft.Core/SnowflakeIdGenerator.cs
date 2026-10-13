namespace PubSoft.Core;

/// <summary>
/// Generates JavaScript-safe 53-bit Snowflake identifiers.
/// Layout: 41-bit timestamp, 5-bit worker ID, 7-bit sequence.
/// </summary>
public sealed class SnowflakeIdGenerator
{
    private const long Epoch = 1704067200000L;
    private const int WorkerIdBits = 5;
    private const int SequenceBits = 7;
    private const long MaxWorkerId = (1L << WorkerIdBits) - 1;
    private const long MaxSequence = (1L << SequenceBits) - 1;
    private const long MaxTimestamp = Epoch + (1L << 41) - 1;
    private const int WorkerIdShift = SequenceBits;
    private const int TimestampShift = SequenceBits + WorkerIdBits;

    private readonly long _workerId;
    private readonly TimeProvider _timeProvider;
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
                throw new InvalidOperationException($"Clock moved backwards by {_lastTimestamp - timestamp} milliseconds.");

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                {
                    if (_lastTimestamp == MaxTimestamp)
                        throw new InvalidOperationException("The Snowflake timestamp range is exhausted.");
                    timestamp = WaitNextMillis(_lastTimestamp);
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

    private long WaitNextMillis(long lastTimestamp)
    {
        var timestamp = ReadTimestamp();
        while (timestamp <= lastTimestamp)
            timestamp = ReadTimestamp();
        return timestamp;
    }
}
