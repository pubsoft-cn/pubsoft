using Microsoft.AspNetCore.Builder;
using PubSoft.Modularity;

namespace PubSoft.AspNetCore;


/// <summary><see cref="WebModuleContext"/> accessors.</summary>
public static class WebModuleContextExtensions
{
    /// <summary>
    /// The <see cref="IApplicationBuilder"/> a module may wire middleware into -- available only
    /// when initialization runs through <see cref="PubSoftModuleAspNetCoreExtensions.UsePubSoftModulesAsync"/>.
    /// </summary>
    public static IApplicationBuilder GetAppBuilder(this ModuleContext context)
        => (context as WebModuleContext)?.App
           ?? throw new InvalidOperationException(
               "No web module context - module middleware wiring requires UsePubSoftModulesAsync on the web host.");
}
