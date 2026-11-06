using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;


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
