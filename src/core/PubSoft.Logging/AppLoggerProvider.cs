using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Logging;

public sealed class AppLoggerProvider(AppLogger appLogger) : ILoggerProvider
{
    private readonly AppLogger _appLogger = appLogger ?? throw new ArgumentNullException(nameof(appLogger));

    public ILogger CreateLogger(string categoryName)
        => new AppLoggerAdapter(categoryName, _appLogger);

    public void Dispose()
    {
    }
}

internal sealed class AppLoggerAdapter(string categoryName, AppLogger appLogger) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => state is IEnumerable<KeyValuePair<string, object?>> properties
            ? LogContext.PushProperties(properties)
            : null;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None && MapLevel(logLevel) >= appLogger.MinLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || formatter is null)
            return;

        try
        {
            var message = formatter(state, exception);
            var context = LogContext.GetCurrentContext();
            if (eventId.Id != 0)
            {
                var enriched = context is null
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    : new Dictionary<string, object?>(context, StringComparer.Ordinal);
                enriched["EventId"] = eventId.Id;
                if (!string.IsNullOrWhiteSpace(eventId.Name))
                    enriched["EventName"] = eventId.Name;
                context = enriched;
            }

            appLogger.Log(MapLevel(logLevel), categoryName, message, exception?.ToString(), context: context);
        }
        catch
        {
        }
    }

    private static AppLogLevel MapLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => AppLogLevel.Trace,
        LogLevel.Debug => AppLogLevel.Debug,
        LogLevel.Information => AppLogLevel.Info,
        LogLevel.Warning => AppLogLevel.Warn,
        LogLevel.Error => AppLogLevel.Error,
        LogLevel.Critical => AppLogLevel.Fatal,
        _ => AppLogLevel.Info
    };
}

public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftLogging(
        this IServiceCollection services,
        AppLogLevel minLevel = AppLogLevel.Info,
        bool enableJsonSink = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        var sinks = new List<ILogSink> { new ConsoleLogSink() };
        if (enableJsonSink)
            sinks.Add(new JsonLogSink(WriteStandardOutputAsync));

        return services.AddPubSoftLogging(new AppLogger(sinks, minLevel));
    }

    public static IServiceCollection AddPubSoftLogging(this IServiceCollection services, AppLogger appLogger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(appLogger);
        services.AddSingleton(appLogger);
        services.AddSingleton<ILoggerProvider, AppLoggerProvider>();
        return services;
    }

    private static async ValueTask WriteStandardOutputAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        await using var stdout = Console.OpenStandardOutput();
        await stdout.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}