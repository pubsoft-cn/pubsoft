namespace PubSoft.Edge;


/// <summary>
/// Signing-key provider -- implemented by the host; returns the secret key for a clientId.
/// </summary>
public interface IEdgeSignKeyProvider
{

    /// <summary>Expose the signing payload in error responses (debug only; keep false in production).</summary>
    bool ShowSignDebug { get; }
    /// <summary>Gets the signing secret for a clientId.</summary>
    string? GetSecretKey(string clientId);
}
