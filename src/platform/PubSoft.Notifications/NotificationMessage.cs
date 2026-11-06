using System.Collections.ObjectModel;

namespace PubSoft.Notifications;

public enum NotificationSeverity
{
    Info,
    Warning,
    Critical
}

public sealed class NotificationMessage
{

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
    public string? Title { get; }
    public string Content { get; }
    public IReadOnlyList<string> Recipients { get; }
    public string? TemplateCode { get; }
    public IReadOnlyDictionary<string, string> TemplateParameters { get; }
    public NotificationSeverity Severity { get; }
    public string? Source { get; }
    public string? CorrelationId { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record NotificationResult(bool Success, string? Message = null, string? ProviderMessageId = null)
{
    public static NotificationResult Ok(string? providerMessageId = null)
        => new(true, ProviderMessageId: providerMessageId);

    public static NotificationResult Fail(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new NotificationResult(false, message.Trim());
    }
}

public interface INotificationChannel
{
    string Name { get; }
    Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

public sealed class NotificationOptions
{
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromMinutes(5);
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

public sealed record NotificationDispatchReport(
    IReadOnlyDictionary<string, NotificationResult> Results)
{
    public bool Succeeded => Results.Values.All(result => result.Success);
}