namespace PubSoft.Auditing;


/// <summary>Bounds and filters for entity-change capture.</summary>
public sealed class AuditChangeCollectorOptions
{
    /// <summary>Whether old/new values are stored (false = property names only).</summary>
    public bool CaptureValues { get; set; }
    /// <summary>Max entity changes per scope; overflow is dropped.</summary>
    public int MaxChanges { get; set; } = 1_000;
    /// <summary>Max properties captured per entity.</summary>
    public int MaxChangedPropertiesPerEntity { get; set; } = 50;
    /// <summary>String values are truncated beyond this length.</summary>
    public int MaxStringValueLength { get; set; } = 2_048;
    /// <summary>Entity types never audited (audit/outbox tables by default).</summary>
    public HashSet<string> IgnoredEntityTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "AuditLog",
        "AuditEntityChange",
        "AuditLogRecord",
        "AuditEntityChangeRecord",
        "OutboxMessage"
    };
    /// <summary>Property names whose values are masked.</summary>
    public HashSet<string> SensitiveProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "PasswordHash", "OldPassword", "NewPassword", "Secret", "ClientSecret",
        "AppSecret", "PrivateKey", "PrivateKeyPem", "Token", "AccessToken", "RefreshToken",
        "IdToken", "SecurityStamp", "ApiKey", "AccessKey", "SecretKey", "Credential",
        "Credentials", "SmsCode", "VerifyCode", "Captcha", "Ticket", "AesKey"
    };

    /// <summary>Validates bounds and returns a defensive copy.</summary>
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
