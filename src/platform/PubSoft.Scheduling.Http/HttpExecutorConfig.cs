using PubSoft.Resilience;

namespace PubSoft.Scheduling.Http;

public sealed class HttpExecutorConfig
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = "";
    public TimeSpan? Timeout { get; set; }
    public int[] ExpectStatusCodes { get; set; } = [200];
    public RetryPolicy? Retry { get; set; }
    public bool AllowUnsafeRetries { get; set; }
    public TokenRef? Token { get; set; }
}

public sealed class TokenRef
{
    public string CredentialKey { get; set; } = "";
    public string HeaderName { get; set; } = "Authorization";
    public string HeaderPrefix { get; set; } = "Bearer ";
}

public sealed class HttpExecutorOptions
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(8);
    public int CircuitFailureThreshold { get; set; } = 5;
    public TimeSpan CircuitOpenTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Allowed target hosts for job URLs (SSRF egress control). Empty = no allowlist
    /// (still subject to <see cref="BlockPrivateNetworks"/>). Entries are exact hosts
    /// (<c>api.example.com</c>) or suffix wildcards (<c>.example.com</c> /
    /// <c>*.example.com</c> — matches subdomains and the bare domain).
    /// </summary>
    public IList<string> AllowedHosts { get; } = new List<string>();

    /// <summary>
    /// When true (default), job URLs resolving to non-public addresses — RFC1918,
    /// loopback, link-local (incl. cloud metadata 169.254.x.x), CGNAT, multicast,
    /// reserved/transition ranges — are refused at connect time. Set false only for
    /// schedulers that legitimately probe inside their own network.
    /// </summary>
    public bool BlockPrivateNetworks { get; set; } = true;

    /// <summary>
    /// Follow 3xx redirects. Default false — a redirect can bounce an allowed host to
    /// an internal target, so redirects are returned as the job result instead.
    /// </summary>
    public bool FollowRedirects { get; set; }

    /// <summary>
    /// Use the system/environment proxy. Default false: egress connects directly,
    /// keeping the private-address check authoritative over the resolved target.
    /// </summary>
    public bool UseSystemProxy { get; set; }

    internal bool IsValid()
        => DefaultTimeout > TimeSpan.Zero
            && CircuitFailureThreshold > 0
            && CircuitOpenTimeout > TimeSpan.Zero
            && AllowedHosts.All(entry => IsValidHostEntry(entry));

    private static bool IsValidHostEntry(string entry)
    {
        var host = entry.StartsWith("*.", StringComparison.Ordinal) ? entry[2..]
            : entry.StartsWith('.') ? entry[1..] : entry;
        return host.Length > 0 && Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }
}