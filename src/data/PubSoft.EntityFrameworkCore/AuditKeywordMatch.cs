namespace PubSoft.EntityFrameworkCore.Auditing;

/// <summary>How the audit-list keyword filter matches <c>RequestPath</c>, <c>TargetName</c>, and <c>Detail</c>.</summary>
public enum AuditKeywordMatch
{
    /// <summary>Unindexable <c>LIKE '%x%'</c> scan - convenient, but expensive on large tables.</summary>
    Substring,
    /// <summary>Left-anchored <c>LIKE 'x%'</c> match - the filtered columns stay indexable.</summary>
    Prefix
}
