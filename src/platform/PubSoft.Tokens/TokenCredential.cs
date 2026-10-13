namespace PubSoft.Tokens;

public sealed class TokenCredential
{
    public string Method { get; set; } = "POST";
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = "";
    public string TokenPath { get; set; } = "token";
    public TimeSpan Expiry { get; set; } = TimeSpan.FromHours(12);
    public string? ExpiryPath { get; set; }
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

public sealed class TokenManagerOptions
{
    public Dictionary<string, TokenCredential> Credentials { get; set; } = new(StringComparer.Ordinal);

    internal bool IsValid(out string error)
    {
        if (Credentials is null)
        {
            error = "Credentials cannot be null.";
            return false;
        }

        foreach (var (key, credential) in Credentials)
        {
            if (string.IsNullOrWhiteSpace(key) || credential is null
                || !HttpTokenProvider.IsValidCredential(credential))
            {
                error = "Each token credential requires a valid key, HTTP(S) URL, method, paths, and positive timing options.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}

public interface ITokenProvider
{
    Task<string?> GetTokenAsync(string credentialKey, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string credentialKey, CancellationToken cancellationToken = default);
}

public sealed class TokenHandlerOptions
{
    public string? DefaultCredentialKey { get; set; }
    public string? DefaultHeaderName { get; set; } = "Authorization";
    public string? DefaultHeaderPrefix { get; set; } = "Bearer ";
}