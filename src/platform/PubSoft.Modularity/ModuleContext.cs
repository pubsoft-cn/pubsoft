using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Modularity;

public sealed class ModuleContext
{
    public required IServiceCollection Services { get; init; }
    public IServiceProvider? ServiceProvider { get; init; }
    public ILogger? Logger { get; init; }
}
