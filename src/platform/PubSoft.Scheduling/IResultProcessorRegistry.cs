namespace PubSoft.Scheduling;


/// <summary>Resolves result processors by name.</summary>
public interface IResultProcessorRegistry
{
    /// <summary>Finds a processor by name.</summary>
    IResultProcessor? Resolve(string name);
    /// <summary>Adds a processor.</summary>
    void Register(IResultProcessor processor);
}
