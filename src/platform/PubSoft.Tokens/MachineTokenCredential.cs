using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PubSoft.Authentication.Jwt;

namespace PubSoft.Tokens;

/// <summary>Per-key claim/key overrides for machine tokens.</summary>
public sealed class MachineTokenCredential
{
    /// <summary>Extra claims for this credential.</summary>
    public Dictionary<string, string> Claims { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Per-credential signing key override.</summary>
    public string? SigningKey { get; set; }
    /// <summary>Per-credential key id override.</summary>
    public string? KeyId { get; set; }
}
