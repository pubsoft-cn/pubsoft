namespace PubSoft.Contracts;

/// <summary>Maps CLR property names onto wire names.</summary>
public interface INamingPolicy
{
    /// <summary>Converts a property name to its wire form.</summary>
    string ConvertName(string propertyName);
}