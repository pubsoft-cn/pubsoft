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
    public required IApplicationBuilder App { get; init; }
    public IHostEnvironment? Environment { get; init; }
}

public static class WebModuleContextExtensions
{
    /// <summary>
    /// The <see cref="IApplicationBuilder"/> a module may wire middleware into — available only
    /// when initialization runs through <see cref="PubSoftModuleAspNetCoreExtensions.UsePubSoftModulesAsync"/>.
    /// </summary>
    public static IApplicationBuilder GetAppBuilder(this ModuleContext context)
        => (context as WebModuleContext)?.App
           ?? throw new InvalidOperationException(
               "No web module context — module middleware wiring requires UsePubSoftModulesAsync on the web host.");
}

public static class PubSoftModuleAspNetCoreExtensions
{
    /// <summary>
    /// Runs <c>OnInitialization</c> for all registered modules with a <see cref="WebModuleContext"/>
    /// (app, env, configuration, provider, logger, shutdown token filled) and hooks reverse-order
    /// <c>OnShutdown</c> to <see cref="IHostApplicationLifetime.ApplicationStopping"/>.
    /// </summary>
    public static async Task<IApplicationBuilder> UsePubSoftModulesAsync(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var provider = app.ApplicationServices;
        var runtime = provider.GetRequiredService<PubSoftModuleRuntime>();
        runtime.ServiceProvider = provider;
        var lifetime = provider.GetService<IHostApplicationLifetime>();
        var context = new WebModuleContext
        {
            Services = runtime.Services,
            ServiceProvider = provider,
            Configuration = provider.GetService<IConfiguration>(),
            Logger = provider.GetService<ILoggerFactory>()?.CreateLogger("PubSoft.Modularity"),
            ShutdownToken = lifetime?.ApplicationStopping ?? default,
            App = app,
            Environment = provider.GetService<IHostEnvironment>()
        };
        await runtime.Orchestrator.OnInitializationAllAsync(context).ConfigureAwait(false);
        lifetime?.ApplicationStopping.Register(
            () => runtime.Orchestrator.OnShutdownAllAsync(context).AsTask().GetAwaiter().GetResult()); // pubsoft:exempt PUBSOFT017 "intentional sync bridge — callback is sync by contract"
        return app;
    }
}
