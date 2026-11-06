namespace PubSoft.Edge;

/// <summary>
/// Edge error codes -- single vocabulary shared by the gateway and clients.
///
/// Encoding:
/// 10XXX -- gateway-level errors
///   104XX -- client errors (4xx semantics)
///   105XX -- server errors (5xx semantics)
///
/// The gateway always returns HTTP 200; the code lives in the response body.
/// </summary>
public static class EdgeErrorCodes
{
    // ── 10400 request format ──
    /// <summary>Request JSON failed to parse.</summary>
    public const string BadRequest = "10400";
    /// <summary>method is empty.</summary>
    public const string MethodRequired = "10400";
    /// <summary>clientId is empty.</summary>
    public const string ClientIdRequired = "10400";
    /// <summary>params failed to deserialize into the handler's parameter type.</summary>
    public const string ParamsInvalid = "10400";
    /// <summary>Business-layer argument validation failed.</summary>
    public const string ParamsValidationFailed = "10400";
    /// <summary>timestamp outside the accepted tolerance window.</summary>
    public const string TimestampOutOfRange = "10400";

    // ── 10401 authentication ──
    /// <summary>Authentication failed (generic).</summary>
    public const string Unauthorized = "10401";
    /// <summary>Handler does not accept this scheme.</summary>
    public const string SchemeNotAccepted = "10401";
    /// <summary>Unknown scheme.</summary>
    public const string UnknownScheme = "10401";
    /// <summary>No signing key configured for the clientId.</summary>
    public const string ClientNotConfigured = "10401";
    /// <summary>Signature verification failed.</summary>
    public const string SignatureInvalid = "10401";
    /// <summary>nonce is required for signature schemes.</summary>
    public const string NonceRequired = "10401";
    /// <summary>sessionKey is empty.</summary>
    public const string SessionKeyRequired = "10401";
    /// <summary>sessionKey expired or does not exist.</summary>
    public const string SessionKeyInvalid = "10401";
    /// <summary>sessionKey expired.</summary>
    public const string SessionKeyExpired = "10401";
    /// <summary>Exception during authentication.</summary>
    public const string AuthError = "10401";

    // ── 10403 forbidden ──
    /// <summary>Not allowed to access the resource.</summary>
    public const string Forbidden = "10403";
    /// <summary>Session roles do not satisfy the requirement.</summary>
    public const string RoleRequired = "10403";
    /// <summary>Tenant mismatch.</summary>
    public const string TenantMismatch = "10403";

    // ── 10404 not found ──
    /// <summary>method does not exist.</summary>
    public const string MethodNotFound = "10404";
    /// <summary>Business resource not found.</summary>
    public const string NotFound = "10404";

    // ── 10409 conflict ──
    /// <summary>Resource exists / state conflict.</summary>
    public const string Conflict = "10409";
    /// <summary>Duplicate request (nonce already used).</summary>
    public const string DuplicateRequest = "10409";

    // ── 10429 rate limited ──
    /// <summary>Too many requests (total limit).</summary>
    public const string RateLimited = "10429";
    /// <summary>Too many failed requests (failure limit).</summary>
    public const string FailureRateLimited = "10429";

    // ── 10500 server errors ──
    /// <summary>Internal error (generic).</summary>
    public const string InternalError = "10500";
    /// <summary>IEdgeSignKeyProvider not registered.</summary>
    public const string SignServiceNotConfigured = "10500";
    /// <summary>EdgeSessionManager not registered.</summary>
    public const string SessionServiceNotConfigured = "10500";
    /// <summary>Service temporarily unavailable.</summary>
    public const string ServiceUnavailable = "10503";
    /// <summary>Request timed out.</summary>
    public const string GatewayTimeout = "10504";
}

/// <summary>
/// Edge error response helpers.
/// </summary>
public static class EdgeError
{
    /// <summary>400 - malformed request.</summary>
    public static EdgeRpcResponse BadRequest(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.BadRequest, message) with { SubCode = subCode };
    /// <summary>401 - missing or invalid credentials.</summary>
    public static EdgeRpcResponse Unauthorized(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.Unauthorized, message) with { SubCode = subCode };
    /// <summary>403 - authenticated but not permitted.</summary>
    public static EdgeRpcResponse Forbidden(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.Forbidden, message) with { SubCode = subCode };
    /// <summary>404 - resource not found.</summary>
    public static EdgeRpcResponse NotFound(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.NotFound, message) with { SubCode = subCode };
    /// <summary>409 - state conflict.</summary>
    public static EdgeRpcResponse Conflict(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.Conflict, message) with { SubCode = subCode };
    /// <summary>429 - rate limit exceeded.</summary>
    public static EdgeRpcResponse RateLimited(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.RateLimited, message) with { SubCode = subCode };
    /// <summary>500 - internal failure.</summary>
    public static EdgeRpcResponse InternalError(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.InternalError, message) with { SubCode = subCode };
    /// <summary>503 - downstream unavailable.</summary>
    public static EdgeRpcResponse ServiceUnavailable(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.ServiceUnavailable, message) with { SubCode = subCode };
    /// <summary>504 - downstream timeout.</summary>
    public static EdgeRpcResponse GatewayTimeout(string message, string? subCode = null) => EdgeRpcResponse.Error(EdgeErrorCodes.GatewayTimeout, message) with { SubCode = subCode };
}
