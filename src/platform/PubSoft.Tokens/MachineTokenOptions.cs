using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PubSoft.Authentication.Jwt;

namespace PubSoft.Tokens;


public sealed class MachineTokenOptions
{
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public required string SigningKey { get; set; }
    public string KeyId { get; set; } = "machine";
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromMinutes(5);
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
        var length = System.Text.Encoding.UTF8.GetByteCount(key);
        return length is >= 32 and <= 128;
    }
}
