using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;

public interface IEdgeSchemePolicy
{
    /// <summary>Returns the pinned scheme for a clientId, or null to allow any scheme.</summary>
    EdgeAuthScheme? GetAllowedScheme(string clientId);
}
