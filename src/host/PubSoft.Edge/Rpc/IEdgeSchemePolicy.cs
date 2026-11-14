namespace PubSoft.Edge;

/// <summary>Host hook deciding which edge auth schemes a request may use.</summary>
public interface IEdgeSchemePolicy
{
    /// <summary>Returns the pinned scheme for a clientId, or null to allow any scheme.</summary>
    EdgeAuthScheme? GetAllowedScheme(string clientId);
}
