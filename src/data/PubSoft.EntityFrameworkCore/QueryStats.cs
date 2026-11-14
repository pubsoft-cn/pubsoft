namespace PubSoft.EntityFrameworkCore;


internal sealed class QueryStats(string operation, string fingerprint, int sampleCapacity)
{
    private readonly object _sampleLock = new();
    private readonly double[] _samples = new double[sampleCapacity];
    private int _sampleCount;
    private long _count;
    private long _errorCount;
    private long _totalTicks;
    private long _maxTicks;

    public long Count => Interlocked.Read(ref _count);

    public void Record(TimeSpan elapsed, bool success)
    {
        var count = Interlocked.Increment(ref _count);
        if (!success)
            Interlocked.Increment(ref _errorCount);
        Interlocked.Add(ref _totalTicks, elapsed.Ticks);
        UpdateMaximum(elapsed.Ticks);

        lock (_sampleLock)
        {
            if (_sampleCount < _samples.Length)
            {
                _samples[_sampleCount++] = elapsed.TotalMilliseconds;
            }
            else
            {
                var replaceIndex = Random.Shared.NextInt64(count);
                if (replaceIndex < _samples.Length)
                    _samples[(int)replaceIndex] = elapsed.TotalMilliseconds;
            }
        }
    }

    public QuerySnapshot GetSnapshot()
    {
        double[] samples;
        lock (_sampleLock)
            samples = _samples.AsSpan(0, _sampleCount).ToArray();
        Array.Sort(samples);

        var count = Interlocked.Read(ref _count);
        var totalTicks = Interlocked.Read(ref _totalTicks);
        return new QuerySnapshot(
            operation,
            fingerprint,
            count,
            Interlocked.Read(ref _errorCount),
            count == 0 ? 0 : TimeSpan.FromTicks(totalTicks).TotalMilliseconds / count,
            Percentile(samples, 0.50),
            Percentile(samples, 0.95),
            Percentile(samples, 0.99),
            TimeSpan.FromTicks(Interlocked.Read(ref _maxTicks)).TotalMilliseconds,
            TimeSpan.FromTicks(totalTicks).TotalMilliseconds);
    }

    public void Reset()
    {
        lock (_sampleLock)
        {
            _sampleCount = 0;
            Array.Clear(_samples);
        }
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _errorCount, 0);
        Interlocked.Exchange(ref _totalTicks, 0);
        Interlocked.Exchange(ref _maxTicks, 0);
    }

    private void UpdateMaximum(long candidate)
    {
        var current = Interlocked.Read(ref _maxTicks);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref _maxTicks, candidate, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private static double Percentile(double[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
            return 0;
        var index = Math.Clamp((int)Math.Ceiling(percentile * sortedSamples.Length) - 1, 0, sortedSamples.Length - 1);
        return sortedSamples[index];
    }
}
