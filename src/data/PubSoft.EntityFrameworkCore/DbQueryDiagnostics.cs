using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;


/// <summary>Query-execution diagnostics: fingerprints, percentiles, and slow-query capture.</summary>
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

    /// <summary>Creates the diagnostics collector.</summary>
    public DbQueryDiagnostics(DbQueryDiagnosticsOptions? options = null, ILogger<DbQueryDiagnostics>? logger = null)
    {
        _options = (options ?? new DbQueryDiagnosticsOptions()).CloneAndValidate();
        _logger = logger;
        _overflowStats = new QueryStats("other", "overflow", _options.MaxLatencySamplesPerTemplate);
    }

    /// <summary>Runs the delegate while recording timing/failure stats.</summary>
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

    /// <summary>Runs a non-query command while recording stats.</summary>
    public Task<int> ExecuteNonQueryAsync(
        string operation,
        string sql,
        DbCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(operation, sql, token => command.ExecuteNonQueryAsync(token), cancellationToken);
    }

    /// <summary>Returns the aggregated snapshot.</summary>
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

    /// <summary>Clears captured stats.</summary>
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
