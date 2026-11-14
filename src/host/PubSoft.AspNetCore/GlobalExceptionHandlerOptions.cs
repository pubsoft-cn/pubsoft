namespace PubSoft.AspNetCore;

/// <summary>Exception-type to status-code mapping for the global exception handler.</summary>
public sealed class GlobalExceptionHandlerOptions
{
    /// <summary>Status for domain exceptions.</summary>
    public int DomainExceptionStatusCode { get; set; } = 400;
    /// <summary>Status for argument exceptions.</summary>
    public int ArgumentExceptionStatusCode { get; set; } = 400;
    /// <summary>Status for invalid-operation exceptions.</summary>
    public int InvalidOperationExceptionStatusCode { get; set; } = 400;
    /// <summary>Status for unauthorized access.</summary>
    public int UnauthorizedAccessStatusCode { get; set; } = 401;
    /// <summary>Status for key-not-found.</summary>
    public int KeyNotFoundStatusCode { get; set; } = 404;
    /// <summary>Fallback status.</summary>
    public int DefaultStatusCode { get; set; } = 500;
    /// <summary>Fallback client-facing message.</summary>
    public string DefaultMessage { get; set; } = "server error";
    /// <summary>Client-facing unauthorized message.</summary>
    public string UnauthorizedMessage { get; set; } = "unauthorized";
    /// <summary>Host-added exception-type mappings.</summary>
    public Dictionary<Type, int> CustomMappings { get; } = new();
}
