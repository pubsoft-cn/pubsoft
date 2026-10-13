using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;

public sealed class DbQueryDiagnosticsOptions
{
    public const int MaximumCapacity = 100_000;

    public bool Enabled { get; set; } = true;
    public double SampleRate { get; set; } = 1.0;
    public TimeSpan SlowQueryThreshold { get; set; } = TimeSpan.FromMilliseconds(100);
    public int MaxQueryTemplates { get; set; } = 1024;
    public int MaxLatencySamplesPerTemplate { get; set; } = 512;

    internal DbQueryDiagnosticsOptions CloneAndValidate()
    {
        if (!double.IsFinite(SampleRate) || SampleRate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(SampleRate));
        if (SlowQueryThreshold < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SlowQueryThreshold));
        if (MaxQueryTemplates <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxQueryTemplates));
        if (MaxQueryTemplates > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(MaxQueryTemplates), $"The maximum supported capacity is {MaximumCapacity}.");
        if (MaxLatencySamplesPerTemplate <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxLatencySamplesPerTemplate));
        if (MaxLatencySamplesPerTemplate > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(MaxLatencySamplesPerTemplate), $"The maximum supported capacity is {MaximumCapacity}.");

        return new DbQueryDiagnosticsOptions
        {
            Enabled = Enabled,
            SampleRate = SampleRate,
            SlowQueryThreshold = SlowQueryThreshold,
            MaxQueryTemplates = MaxQueryTemplates,
            MaxLatencySamplesPerTemplate = MaxLatencySamplesPerTemplate
        };
    }
}

public sealed class DbQueryDiagnostics
{
    private readonly ILogger<DbQueryDiagnostics>? _logger;
    private readonly DbQueryDiagnosticsOptions _options;
    private readonly ConcurrentDictionary<string, QueryStats> _stats = new(StringComparer.Ordinal);
    private readonly QueryStats _overflowStats;
    private readonly object _statsLock = new();
    private long _totalQueries;
    private long _totalErrors;
    private long _slowQueryCount;

    public DbQueryDiagnostics(DbQueryDiagnosticsOptions? options = null, ILogger<DbQueryDiagnostics>? logger = null)
    {
        _options = (options ?? new DbQueryDiagnosticsOptions()).CloneAndValidate();
        _logger = logger;
        _overflowStats = new QueryStats("other", "overflow", _options.MaxLatencySamplesPerTemplate);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        string operation,
        string sql,
        Func<CancellationToken, Task<TResult>> executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executor);
        operation = ValidateOperation(operation);
        ArgumentNullException.ThrowIfNull(sql);

        if (!_options.Enabled)
            return await executor(cancellationToken).ConfigureAwait(false);

        var sampled = _options.SampleRate >= 1.0 || Random.Shared.NextDouble() < _options.SampleRate;
        if (!sampled)
            return await executor(cancellationToken).ConfigureAwait(false);

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = await executor(cancellationToken).ConfigureAwait(false);
            Record(operation, sql, Stopwatch.GetElapsedTime(startedAt), success: true, error: null);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Record(operation, sql, Stopwatch.GetElapsedTime(startedAt), success: false, exception);
            throw;
        }
    }

    public Task<int> ExecuteNonQueryAsync(
        string operation,
        string sql,
        DbCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(operation, sql, token => command.ExecuteNonQueryAsync(token), cancellationToken);
    }

    public DiagnosticSnapshot GetSnapshot()
    {
        var snapshots = _stats.Values.Select(stats => stats.GetSnapshot()).ToList();
        if (_overflowStats.Count > 0)
            snapshots.Add(_overflowStats.GetSnapshot());

        return new DiagnosticSnapshot(
            Interlocked.Read(ref _totalQueries),
            Interlocked.Read(ref _totalErrors),
            Interlocked.Read(ref _slowQueryCount),
            snapshots.OrderByDescending(query => query.TotalMs).Take(20).ToArray());
    }

    public void Reset()
    {
        lock (_statsLock)
        {
            _stats.Clear();
            _overflowStats.Reset();
            Interlocked.Exchange(ref _totalQueries, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
            Interlocked.Exchange(ref _slowQueryCount, 0);
        }
    }

    private void Record(string operation, string sql, TimeSpan elapsed, bool success, Exception? error)
    {
        var fingerprint = Fingerprint(sql);
        var stats = GetStats(operation, fingerprint);
        stats.Record(elapsed, success);
        Interlocked.Increment(ref _totalQueries);
        if (!success)
            Interlocked.Increment(ref _totalErrors);

        if (_options.SlowQueryThreshold > TimeSpan.Zero && elapsed >= _options.SlowQueryThreshold)
        {
            Interlocked.Increment(ref _slowQueryCount);
            _logger?.LogWarning(
                "Slow database operation {Operation} ({QueryFingerprint}) took {ElapsedMilliseconds} ms, threshold {ThresholdMilliseconds} ms, error type {ErrorType}",
                operation,
                fingerprint,
                elapsed.TotalMilliseconds,
                _options.SlowQueryThreshold.TotalMilliseconds,
                error?.GetType().Name);
        }
    }

    private QueryStats GetStats(string operation, string fingerprint)
    {
        var key = $"{operation}:{fingerprint}";
        if (_stats.TryGetValue(key, out var existing))
            return existing;

        lock (_statsLock)
        {
            if (_stats.TryGetValue(key, out existing))
                return existing;
            if (_stats.Count >= _options.MaxQueryTemplates)
                return _overflowStats;

            var created = new QueryStats(operation, fingerprint, _options.MaxLatencySamplesPerTemplate);
            _stats.TryAdd(key, created);
            return created;
        }
    }

    private static string ValidateOperation(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (operation.Length > 80 || operation.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '.' and not '-'))
            throw new ArgumentException("Operation names must be at most 80 ASCII letters, digits, dots, dashes, or underscores.", nameof(operation));
        return operation;
    }

    private static string Fingerprint(string sql)
    {
        var sqlBytes = Encoding.UTF8.GetBytes(sql);
        try
        {
            return Convert.ToHexString(SHA256.HashData(sqlBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sqlBytes);
        }
    }
}

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

public sealed record DiagnosticSnapshot(
    long TotalQueries,
    long TotalErrors,
    long SlowQueryCount,
    IReadOnlyList<QuerySnapshot> TopQueries);

public sealed record QuerySnapshot(
    string Operation,
    string QueryFingerprint,
    long Count,
    long ErrorCount,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    double TotalMs);