namespace PubSoft.Auditing;


/// <summary>Writes manual business audit entries into the configured audit sink.</summary>
public interface IAuditLogWriter
{
    /// <summary>Writes one business audit entry.</summary>
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
