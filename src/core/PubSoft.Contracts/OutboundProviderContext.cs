using System.Diagnostics.CodeAnalysis;

namespace PubSoft.Contracts;


public sealed class OutboundProviderContext
{
    public string ProfileId { get; set; } = string.Empty;
    public long TenantId { get; set; }
    public string OperationId { get; set; } = string.Empty;
}
