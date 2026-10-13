using PubSoft.Domain;

namespace PubSoft.Scheduling.EntityFrameworkCore;

public sealed class SchedulerStateRecord : IMultiTenant
{
    public long Id { get; set; }
    public long? TenantId { get; set; }
    public string TenantScope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string ValueJson { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}