namespace PubSoft.Tokens;

/// <summary>HTTP token-fetch recipe for one credential key.</summary>
public sealed class TokenCredential
{
    /// <summary>HTTP method.</summary>
    public string Method { get; set; } = "POST";
    /// <summary>Token endpoint.</summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// Permits plaintext <c>http</c> endpoints for this credential. Credentials travel
    /// over this URL -- non-loopback <c>http</c> is rejected by <see cref="HttpTokenProvider"/>
    /// unless this flag is set. Loopback endpoints are exempt for local development.
    /// </summary>
    public bool AllowInsecureUrl { get; set; }
    /// <summary>Extra request headers.</summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Request body.</summary>
    public string Body { get; set; } = "";
    /// <summary>JSON path to the token in the response.</summary>
    public string TokenPath { get; set; } = "token";
    /// <summary>Token lifetime when no expiry path is given.</summary>
    public TimeSpan Expiry { get; set; } = TimeSpan.FromHours(12);
    /// <summary>JSON path to an expiry hint in the response.</summary>
    public string? ExpiryPath { get; set; }
    /// <summary>Refresh margin before expiry.</summary>
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Fetch timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Named credentials for <see cref="HttpTokenProvider"/>.</summary>
public sealed class TokenManagerOptions
{
    /// <summary>Key → fetch recipe map.</summary>
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

/// <summary>Resolves bearer tokens per credential key.</summary>
public interface ITokenProvider
{
    /// <summary>Returns a usable token (cached or fresh).</summary>
    Task<string?> GetTokenAsync(string credentialKey, CancellationToken cancellationToken = default);
    /// <summary>Drops the cached token.</summary>
    Task InvalidateAsync(string credentialKey, CancellationToken cancellationToken = default);
}

/// <summary>Defaults for <see cref="TokenDelegatingHandler"/>.</summary>
public sealed class TokenHandlerOptions
{
    /// <summary>Default credential key when the request does not set one.</summary>
    public string? DefaultCredentialKey { get; set; }
    /// <summary>Header the token lands in.</summary>
    public string? DefaultHeaderName { get; set; } = "Authorization";
    /// <summary>Value prefix before the token.</summary>
    public string? DefaultHeaderPrefix { get; set; } = "Bearer ";
}