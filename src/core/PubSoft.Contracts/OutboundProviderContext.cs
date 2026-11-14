namespace PubSoft.Contracts;


/// <summary>Execution context passed to an outbound provider.</summary>
public sealed class OutboundProviderContext
{
    /// <summary>Credential/profile selection.</summary>
    public string ProfileId { get; set; } = string.Empty;
    /// <summary>Owning tenant.</summary>
    public long TenantId { get; set; }
    /// <summary>The operation being executed.</summary>
    public string OperationId { get; set; } = string.Empty;
}
