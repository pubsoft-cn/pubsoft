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

    internal bool IsValid()
        => DefaultTimeout > TimeSpan.Zero
            && CircuitFailureThreshold > 0
            && CircuitOpenTimeout > TimeSpan.Zero;
}