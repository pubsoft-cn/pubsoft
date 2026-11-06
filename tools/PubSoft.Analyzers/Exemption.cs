namespace PubSoft.Analyzers;

/// <summary>
/// Explicit per-line opt-out honored by the text-based analyzers: a line carrying
/// <c>pubsoft:exempt PUBSOFT00N "reason"</c> suppresses that diagnostic on that line only.
/// The quoted reason is mandatory — bare markers still report.
/// </summary>
internal static class Exemption
{
    public static bool IsExempt(string line, string diagnosticId)
    {
        var marker = "pubsoft:exempt " + diagnosticId;
        var at = line.IndexOf(marker, System.StringComparison.Ordinal);
        if (at < 0) return false;
        var reason = line.Substring(at + marker.Length);
        var open = reason.IndexOf('"');
        var close = open < 0 ? -1 : reason.IndexOf('"', open + 1);
        return open >= 0 && close > open + 1;
    }
}
