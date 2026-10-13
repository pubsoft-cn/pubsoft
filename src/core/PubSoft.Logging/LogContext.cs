namespace PubSoft.Logging;

public static class LogContext
{
    private static readonly AsyncLocal<Dictionary<string, object?>?> Current = new();

    public static IDisposable PushProperty(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var previous = Current.Value;
        var updated = previous is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(previous, StringComparer.Ordinal);
        updated[key] = value;
        Current.Value = updated;
        return new ContextScope(previous, updated);
    }

    public static IReadOnlyDictionary<string, object?>? GetCurrentContext()
        => Current.Value is { } context
            ? new Dictionary<string, object?>(context, StringComparer.Ordinal)
            : null;

    public static void Clear() => Current.Value = null;

    public static IDisposable PushProperties(IEnumerable<KeyValuePair<string, object?>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var scopes = new List<IDisposable>();
        foreach (var property in properties)
            scopes.Add(PushProperty(property.Key, property.Value));
        return new CompositeScope(scopes);
    }

    private sealed class ContextScope(
        Dictionary<string, object?>? previous,
        Dictionary<string, object?> current) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && ReferenceEquals(Current.Value, current))
                Current.Value = previous;
        }
    }

    private sealed class CompositeScope(IReadOnlyList<IDisposable> scopes) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            for (var index = scopes.Count - 1; index >= 0; index--)
                scopes[index].Dispose();
        }
    }
}