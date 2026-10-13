namespace PubSoft.Contracts;

public interface INamingPolicy
{
    string ConvertName(string propertyName);
}