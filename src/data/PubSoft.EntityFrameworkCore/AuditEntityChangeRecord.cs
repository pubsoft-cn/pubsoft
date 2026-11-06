using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore.Auditing;


public sealed class AuditEntityChangeRecord : IMultiTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuditLogId { get; set; }
    public long? TenantId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditChangeKind Kind { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string PropertiesJson { get; set; } = "[]";
    public AuditLogRecord AuditLog { get; set; } = null!;
}
