namespace PubSoft.Tokens;


/// <summary>Issuer/audience/keys for locally-issued machine tokens.</summary>
public sealed class MachineTokenOptions
{
    /// <summary>Token issuer.</summary>
    public required string Issuer { get; set; }
    /// <summary>Token audience.</summary>
    public required string Audience { get; set; }
    /// <summary>Symmetric signing key (base64).</summary>
    public required string SigningKey { get; set; }
    /// <summary>Default key id.</summary>
    public string KeyId { get; set; } = "machine";
    /// <summary>Token lifetime (max 1 day).</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);
    /// <summary>Refresh margin before expiry.</summary>
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Per-key claim/key overrides.</summary>
    public Dictionary<string, MachineTokenCredential> Credentials { get; set; } = new(StringComparer.Ordinal);

    internal bool IsValid(out string error)
    {
        if (string.IsNullOrWhiteSpace(Issuer) || Issuer.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(Audience) || Audience.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(KeyId) || KeyId.Length > 128 || KeyId.Any(char.IsControl)
            || Lifetime <= TimeSpan.Zero || Lifetime > TimeSpan.FromDays(1)
            || RefreshBefore < TimeSpan.Zero || RefreshBefore >= Lifetime
            || Credentials is null)
        {
            error = "Machine token options require a non-empty issuer/audience/key id, positive lifetime of at most one day, and refresh-before shorter than the lifetime.";
            return false;
        }

        if (!IsValidKeyMaterial(SigningKey))
        {
            error = "Machine token signing keys must be between 256 and 1024 bits.";
            return false;
        }

        foreach (var (key, credential) in Credentials)
        {
            if (string.IsNullOrWhiteSpace(key) || credential is null
                || (credential.KeyId is { } kid && (kid.Length > 128 || kid.Any(char.IsControl)))
                || (credential.SigningKey is { } key2 && !IsValidKeyMaterial(key2)))
            {
                error = "Each machine token credential requires a non-empty key, valid key id, and a 256-1024 bit signing key when overridden.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValidKeyMaterial(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(key);
        }
        catch (FormatException)
        {
            return false;
        }
        return decoded.Length is >= 32 and <= 128;
    }
}
