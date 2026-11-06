using System.Collections.ObjectModel;

namespace PubSoft.Notifications;

/// <summary>Message severity.</summary>
public enum NotificationSeverity
{
    /// <summary>Informational.</summary>
    Info,
    /// <summary>Warning.</summary>
    Warning,
    /// <summary>Critical - page-worthy.</summary>
    Critical
}

/// <summary>One notification to send via channels.</summary>
public sealed class NotificationMessage
{

    /// <summary>Creates a message; content is required.</summary>
    public NotificationMessage(
        string content,
        IEnumerable<string>? recipients = null,
        string? title = null,
        string? templateCode = null,
        IReadOnlyDictionary<string, string>? templateParameters = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        string? source = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));

        var recipientSnapshot = recipients?.Select(recipient =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
            return recipient.Trim();
        }).ToArray() ?? [];

        Content = content.Trim();
        Recipients = Array.AsReadOnly(recipientSnapshot);
        Title = NormalizeOptional(title);
        TemplateCode = NormalizeOptional(templateCode);
        TemplateParameters = new ReadOnlyDictionary<string, string>(
            templateParameters is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(templateParameters, StringComparer.Ordinal));
        Severity = severity;
        Source = NormalizeOptional(source);
        CorrelationId = NormalizeOptional(correlationId);
    }
    /// <summary>Optional title.</summary>
    public string? Title { get; }
    /// <summary>Message body.</summary>
    public string Content { get; }
    /// <summary>Target recipients.</summary>
    public IReadOnlyList<string> Recipients { get; }
    /// <summary>Channel template key.</summary>
    public string? TemplateCode { get; }
    /// <summary>Template parameter values.</summary>
    public IReadOnlyDictionary<string, string> TemplateParameters { get; }
    /// <summary>Severity.</summary>
    public NotificationSeverity Severity { get; }
    /// <summary>Originating component.</summary>
    public string? Source { get; }
    /// <summary>Correlation id.</summary>
    public string? CorrelationId { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Channel send outcome.</summary>
/// <param name="Success">Whether the send succeeded.</param>
/// <param name="Message">Failure detail.</param>
/// <param name="ProviderMessageId">Provider-assigned message id.</param>
public sealed record NotificationResult(bool Success, string? Message = null, string? ProviderMessageId = null)
{
    /// <summary>Creates a success result.</summary>
    public static NotificationResult Ok(string? providerMessageId = null)
        => new(true, ProviderMessageId: providerMessageId);

    /// <summary>Creates a failure result.</summary>
    public static NotificationResult Fail(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new NotificationResult(false, message.Trim());
    }
}

/// <summary>One notification delivery channel.</summary>
public interface INotificationChannel
{
    /// <summary>Channel name.</summary>
    string Name { get; }
    /// <summary>Sends the message.</summary>
    Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Dispatcher options.</summary>
public sealed class NotificationOptions
{
    /// <summary>Identical messages within this window are suppressed.</summary>
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Dedupe table cap.</summary>
    public int MaximumDeduplicationEntries { get; set; } = 10_000;

    internal NotificationOptions CloneAndValidate()
    {
        if (DeduplicationWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DeduplicationWindow));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDeduplicationEntries);
        return new NotificationOptions
        {
            DeduplicationWindow = DeduplicationWindow,
            MaximumDeduplicationEntries = MaximumDeduplicationEntries
        };
    }
}

/// <summary>Per-channel outcome of a multi-channel dispatch.</summary>
/// <param name="Results">Channel name → result.</param>
public sealed record NotificationDispatchReport(
    IReadOnlyDictionary<string, NotificationResult> Results)
{
    /// <summary>Whether every channel succeeded.</summary>
    public bool Succeeded => Results.Values.All(result => result.Success);
}