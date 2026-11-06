using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PubSoft.Authentication.Jwt;

namespace PubSoft.Tokens;

public sealed class MachineTokenCredential
{
    public Dictionary<string, string> Claims { get; set; } = new(StringComparer.Ordinal);
    public string? SigningKey { get; set; }
    public string? KeyId { get; set; }
}
