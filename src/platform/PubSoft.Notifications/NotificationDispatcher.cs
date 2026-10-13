using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Notifications;

public sealed class NotificationDispatcher
{
    private readonly IReadOnlyDictionary<string, INotificationChannel> _channels;
    private readonly NotificationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationDispatcher>? _logger;
    private readonly object _deduplicationLock = new();
    private readonly Dictionary<DeduplicationKey, DateTimeOffset?> _recent = [];

    public NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        IOptions<NotificationOptions>? options = null,
        ILogger<NotificationDispatcher>? logger = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = (options?.Value ?? new NotificationOptions()).CloneAndValidate();
        _logger = logger;

        var channelMap = new Dictionary<string, INotificationChannel>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in channels)
        {
            ArgumentNullException.ThrowIfNull(channel);
            ArgumentException.ThrowIfNullOrWhiteSpace(channel.Name);
            var name = channel.Name.Trim();
            if (!channelMap.TryAdd(name, channel))
                throw new ArgumentException($"A notification channel named '{name}' is already registered.", nameof(channels));
        }

        _channels = new ReadOnlyDictionary<string, INotificationChannel>(channelMap);
    }

    public async Task<NotificationResult> DispatchAsync(
        string channelName,
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedName = channelName.Trim();
        if (!_channels.TryGetValue(normalizedName, out var channel))
        {
            _logger?.LogWarning("Notification channel {Channel} is not registered.", normalizedName);
            return NotificationResult.Fail($"Channel '{normalizedName}' not registered");
        }

        DeduplicationKey? key = null;
        if (_options.DeduplicationWindow > TimeSpan.Zero && message.CorrelationId is { } correlationId)
        {
            key = new DeduplicationKey(channel.Name.Trim(), correlationId);
            if (!TryReserve(key.Value, _timeProvider.GetUtcNow(), out var suppressed))
                return NotificationResult.Fail("Notification deduplication capacity is full.");
            if (suppressed)
            {
                _logger?.LogDebug("Notification suppressed for channel {Channel} and correlation {CorrelationId}.",
                    normalizedName, correlationId);
                return NotificationResult.Ok("suppressed");
            }
        }

        NotificationResult result;
        try
        {
            result = await channel.SendAsync(message, cancellationToken).ConfigureAwait(false)
                ?? NotificationResult.Fail("Notification channel returned no result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (key is { } canceledKey)
                ReleaseReservation(canceledKey);
            throw;
        }
        catch (Exception exception)
        {
            if (key is { } failedKey)
                ReleaseReservation(failedKey);
            _logger?.LogError(exception, "Notification channel {Channel} failed.", normalizedName);
            return NotificationResult.Fail("Notification channel failed.");
        }

        if (key is { } completedKey)
        {
            if (result.Success)
                CompleteReservation(completedKey, AddWindowSaturating(_timeProvider.GetUtcNow(), _options.DeduplicationWindow));
            else
                ReleaseReservation(completedKey);
        }

        return result;
    }

    public async Task<NotificationDispatchReport> DispatchToAllAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var results = new Dictionary<string, NotificationResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var channelName in _channels.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[channelName] = await DispatchAsync(channelName, message, cancellationToken).ConfigureAwait(false);
        }

        return new NotificationDispatchReport(new ReadOnlyDictionary<string, NotificationResult>(results));
    }

    private bool TryReserve(DeduplicationKey key, DateTimeOffset now, out bool suppressed)
    {
        lock (_deduplicationLock)
        {
            foreach (var expired in _recent
                .Where(pair => pair.Value is { } expiry && expiry <= now)
                .Select(pair => pair.Key)
                .ToArray())
                _recent.Remove(expired);

            if (_recent.TryGetValue(key, out var expiry))
            {
                suppressed = expiry is null || expiry > now;
                return true;
            }

            if (_recent.Count >= _options.MaximumDeduplicationEntries)
            {
                suppressed = false;
                return false;
            }

            _recent.Add(key, null);
            suppressed = false;
            return true;
        }
    }

    private void CompleteReservation(DeduplicationKey key, DateTimeOffset expiresAt)
    {
        lock (_deduplicationLock)
        {
            if (_recent.ContainsKey(key))
                _recent[key] = expiresAt;
        }
    }

    private void ReleaseReservation(DeduplicationKey key)
    {
        lock (_deduplicationLock)
            _recent.Remove(key);
    }

    private static DateTimeOffset AddWindowSaturating(DateTimeOffset timestamp, TimeSpan window)
        => window > DateTimeOffset.MaxValue - timestamp ? DateTimeOffset.MaxValue : timestamp + window;

    private readonly record struct DeduplicationKey(string Channel, string CorrelationId);
}

public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftNotifications(
        this IServiceCollection services,
        Action<NotificationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null)
            services.Configure(configure);
        services.AddSingleton<NotificationDispatcher>();
        return services;
    }
}