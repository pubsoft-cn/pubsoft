using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Interceptor logging commands exceeding the slow-query threshold.</summary>
public class SlowQueryInterceptor : DbCommandInterceptor
{
    private readonly TimeSpan _slowQueryThreshold;
    private readonly ILogger<SlowQueryInterceptor>? _logger;

    /// <summary>Creates the interceptor.</summary>
    public SlowQueryInterceptor(TimeSpan slowQueryThreshold, ILogger<SlowQueryInterceptor>? logger = null)
    {
        _slowQueryThreshold = slowQueryThreshold;
        _logger = logger;
    }

    /// <inheritdoc />
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    /// <inheritdoc />
    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        LogIfSlow(command, eventData.Duration);
        return base.NonQueryExecuted(command, eventData, result);
    }

    /// <inheritdoc />
    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ScalarExecuted(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogIfSlow(DbCommand command, TimeSpan duration)
    {
        if (_logger is null || duration < _slowQueryThreshold)
            return;

        var sql = command.CommandText;
        if (sql.Length > 500)
            sql = sql[..500] + "...(truncated)";

        _logger.LogWarning(
            "[SLOW QUERY] {Duration:F1}ms (threshold={Threshold:F0}ms) | SQL: {Sql}",
            duration.TotalMilliseconds, _slowQueryThreshold.TotalMilliseconds, sql);
    }
}
