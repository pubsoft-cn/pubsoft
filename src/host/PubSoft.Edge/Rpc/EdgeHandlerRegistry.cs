namespace PubSoft.Edge;


/// <summary>
/// Edge handler registry -- DI-backed method -> handler map.
/// </summary>
public sealed class EdgeHandlerRegistry
{
    private readonly Dictionary<string, IEdgeHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>Looks up the handler for a method name.</summary>
    public bool TryGetHandler(string method, out IEdgeHandler handler)
    {
        return _handlers.TryGetValue(method, out handler!);
    }

    internal void Register(IEdgeHandler handler)
    {
        _handlers[handler.Method] = handler;
    }
}
