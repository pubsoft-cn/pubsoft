using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;


/// <summary>
/// Edge handler registry — DI-backed method → handler map.
/// </summary>
public sealed class EdgeHandlerRegistry
{
    private readonly Dictionary<string, IEdgeHandler> _handlers = new(StringComparer.Ordinal);

    public bool TryGetHandler(string method, out IEdgeHandler handler)
    {
        return _handlers.TryGetValue(method, out handler!);
    }

    internal void Register(IEdgeHandler handler)
    {
        _handlers[handler.Method] = handler;
    }
}
