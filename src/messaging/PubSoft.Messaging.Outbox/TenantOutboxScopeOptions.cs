namespace PubSoft.Messaging.Outbox;

/// <summary>Bounds and shape for catalog-backed tenant outbox enumeration.</summary>
public sealed class TenantOutboxScopeOptions
{
    /// <summary>Whether a host (null tenant) scope is processed before tenant scopes. Default false.</summary>
    public bool IncludeHostScope { get; set; }

    /// <summary>Maximum enabled tenants processed in one poll. Default 256, maximum 4096; exceeding the bound fails the poll rather than silently skipping tenants.</summary>
    public int MaxTenantsPerPoll { get; set; } = 256;

    /// <summary>Validates the bound.</summary>
    internal bool IsValid(out string error)
    {
        if (MaxTenantsPerPoll is < 1 or > 4096)
        {
            error = "MaxTenantsPerPoll must be between 1 and 4096.";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
