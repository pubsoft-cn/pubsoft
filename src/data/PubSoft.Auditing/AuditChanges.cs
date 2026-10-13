using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;

public enum AuditChangeKind
{
    Created,
    Updated,
    Deleted
}

public sealed record AuditPropertyChange(string Name, JsonElement? OldValue, JsonElement? NewValue);

public sealed record AuditEntityChange(
    string EntityType,
    string EntityId,
    AuditChangeKind Kind,
    DateTimeOffset ChangedAt,
    IReadOnlyList<AuditPropertyChange> Properties);

public interface IAuditChangeCollector
{
    IReadOnlyList<AuditEntityChange> GetSnapshot();
    void Add(AuditEntityChange change);
    void AddRange(IReadOnlyCollection<AuditEntityChange> changes);
    void Clear();
}

public sealed class AuditChangeCollectorOptions
{
    public bool CaptureValues { get; set; }
    public int MaxChanges { get; set; } = 1_000;
    public int MaxChangedPropertiesPerEntity { get; set; } = 50;
    public int MaxStringValueLength { get; set; } = 2_048;
    public HashSet<string> IgnoredEntityTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "AuditLog",
        "AuditEntityChange",
        "AuditLogRecord",
        "AuditEntityChangeRecord",
        "OutboxMessage"
    };
    public HashSet<string> SensitiveProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "PasswordHash", "OldPassword", "NewPassword", "Secret", "ClientSecret",
        "AppSecret", "PrivateKey", "PrivateKeyPem", "Token", "AccessToken", "RefreshToken",
        "IdToken", "SecurityStamp", "ApiKey", "AccessKey", "SecretKey", "Credential",
        "Credentials", "SmsCode", "VerifyCode", "Captcha", "Ticket", "AesKey"
    };

    public AuditChangeCollectorOptions CloneAndValidate()
    {
        if (MaxChanges is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(MaxChanges));
        if (MaxChangedPropertiesPerEntity is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(MaxChangedPropertiesPerEntity));
        if (MaxStringValueLength is < 1 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaxStringValueLength));

        return new AuditChangeCollectorOptions
        {
            CaptureValues = CaptureValues,
            MaxChanges = MaxChanges,
            MaxChangedPropertiesPerEntity = MaxChangedPropertiesPerEntity,
            MaxStringValueLength = MaxStringValueLength,
            IgnoredEntityTypes = new HashSet<string>(IgnoredEntityTypes, StringComparer.OrdinalIgnoreCase),
            SensitiveProperties = new HashSet<string>(SensitiveProperties, StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class AuditChangeCollector : IAuditChangeCollector
{
    private readonly object _gate = new();
    private readonly List<AuditEntityChange> _changes = [];
    private readonly int _maximumChanges;

    public AuditChangeCollector(AuditChangeCollectorOptions? options = null)
        => _maximumChanges = (options ?? new AuditChangeCollectorOptions()).CloneAndValidate().MaxChanges;

    public IReadOnlyList<AuditEntityChange> GetSnapshot()
    {
        lock (_gate)
            return new ReadOnlyCollection<AuditEntityChange>(_changes.ToArray());
    }

    public void Add(AuditEntityChange change)
        => AddRange([change]);

    public void AddRange(IReadOnlyCollection<AuditEntityChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        lock (_gate)
        {
            if (changes.Count > _maximumChanges - _changes.Count)
                throw new InvalidOperationException($"The audit change limit of {_maximumChanges} was reached.");
            _changes.AddRange(changes);
        }
    }

    public void Clear()
    {
        lock (_gate)
            _changes.Clear();
    }
}

public static class AuditValueSerializer
{
    [RequiresUnreferencedCode("Audit values are serialized by runtime type; the EF audit pipeline is not a trimming/AOT scenario.")]
    [RequiresDynamicCode("Audit values are serialized by runtime type; the EF audit pipeline is not a Native AOT scenario.")]
    public static JsonElement? Serialize(object? value, int maxStringLength)
    {
        if (value is null)
            return JsonSerializer.SerializeToElement<object?>(null);

        if (value is string text)
            value = text.Length > maxStringLength ? string.Concat(text.AsSpan(0, maxStringLength), "...") : text;

        var type = value.GetType();
        if (!(type.IsPrimitive || type.IsEnum || value is string or decimal or DateTime or DateTimeOffset or Guid or TimeSpan))
            return null;

        return JsonSerializer.SerializeToElement(value, type);
    }
}