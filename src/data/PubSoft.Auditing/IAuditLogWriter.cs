namespace PubSoft.Auditing;


/// <summary>Writes manual business audit entries into the configured audit sink.</summary>
public interface IAuditLogWriter
{
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
