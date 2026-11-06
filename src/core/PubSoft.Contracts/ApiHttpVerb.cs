namespace PubSoft.Contracts;

/// <summary>HTTP verbs supported by outbound contracts.</summary>
public enum ApiHttpVerb : byte
{
    /// <summary>HTTP GET.</summary>
    Get,
    /// <summary>HTTP POST.</summary>
    Post,
    /// <summary>HTTP PUT.</summary>
    Put,
    /// <summary>HTTP DELETE.</summary>
    Delete
}