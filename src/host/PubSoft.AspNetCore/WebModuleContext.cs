using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.Modularity;

namespace PubSoft.AspNetCore;

/// <summary>A <see cref="ModuleContext"/> that also carries the web pipeline being built.</summary>
public sealed class WebModuleContext : ModuleContext
{
    /// <summary>The application builder.</summary>
    public required IApplicationBuilder App { get; init; }
    /// <summary>Host environment.</summary>
    public IHostEnvironment? Environment { get; init; }
}
