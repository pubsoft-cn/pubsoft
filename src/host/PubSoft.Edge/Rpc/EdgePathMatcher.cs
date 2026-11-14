namespace PubSoft.Edge;


/// <summary>
/// Shared gateway-path matcher -- exact <c>{gatewayPath}</c> match, or the single-segment
/// pseudo-path <c>{gatewayPath}/{method}</c> when pseudo paths are enabled.
/// Used by both the gateway and the rate-limit middleware so pseudo-path traffic
/// cannot slip past the limiter.
/// </summary>
internal static class EdgePathMatcher
{
    public static bool TryMatch(string? rawPath, string gatewayPath, bool allowPseudo, out string? pseudoMethod)
    {
        pseudoMethod = null;
        var path = rawPath?.TrimEnd('/') ?? string.Empty;
        if (path.Equals(gatewayPath, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!allowPseudo)
            return false;
        if (!path.StartsWith(gatewayPath + "/", StringComparison.OrdinalIgnoreCase))
            return false;
        var tail = path[(gatewayPath.Length + 1)..];
        if (tail.Length == 0 || tail.Contains('/'))
            return false;
        pseudoMethod = tail;
        return true;
    }
}
