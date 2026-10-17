using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp;
using Volo.Abp.Modularity;

namespace PubSoft.Abp.AspNetCore;

/// <summary>
/// Lightweight ABP module bootstrap for <see cref="WebApplication"/> — runs a startup
/// <see cref="AbpModule"/> graph on Volo.Abp.Core's <c>AbpApplicationFactory</c>
/// without taking a dependency on Volo.Abp.AspNetCore.
/// </summary>
public static class PubSoftApplicationBuilderExtensions
{
    private static WebApplication? s_webApp;

    /// <summary>
    /// Creates and registers the ABP application (equivalent of Volo.Abp.AspNetCore's
    /// <c>builder.AddApplicationAsync&lt;T&gt;()</c>).
    /// </summary>
    public static async Task AddPubSoftApplicationAsync<TStartupModule>(
        this WebApplicationBuilder builder,
        Action<AbpApplicationCreationOptions>? optionsAction = null)
        where TStartupModule : IAbpModule
    {
        ArgumentNullException.ThrowIfNull(builder);

        var application = await AbpApplicationFactory.CreateAsync<TStartupModule>(
            builder.Services,
            options =>
            {
                options.Services.ReplaceConfiguration(builder.Configuration);
                optionsAction?.Invoke(options);
            });
        builder.Services.AddSingleton<IAbpApplicationWithExternalServiceProvider>(application);
        builder.Services.AddSingleton(application);
    }

    /// <summary>
    /// Initializes the ABP application — runs module <c>OnApplicationInitialization</c>
    /// where the middleware pipeline is configured.
    /// </summary>
    public static async Task InitializePubSoftApplicationAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        s_webApp = app;
        var application = app.Services.GetRequiredService<IAbpApplicationWithExternalServiceProvider>();
        await application.InitializeAsync(app.Services);
    }

    /// <summary>Shuts the ABP application down.</summary>
    public static async Task ShutdownPubSoftApplicationAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var application = app.Services.GetRequiredService<IAbpApplicationWithExternalServiceProvider>();
        await application.ShutdownAsync();
    }

    internal static WebApplication? WebApp => s_webApp;
}

/// <summary>
/// <see cref="ApplicationInitializationContext"/> helpers — replacements for
/// Volo.Abp.AspNetCore's <c>GetApplicationBuilder</c>/<c>GetEnvironment</c>.
/// </summary>
public static class PubSoftApplicationInitializationContextExtensions
{
    /// <summary>Gets the host <see cref="IApplicationBuilder"/> for the running app.</summary>
    public static IApplicationBuilder GetAppBuilder(this ApplicationInitializationContext context)
        => PubSoftApplicationBuilderExtensions.WebApp!;

    /// <summary>Gets the host <see cref="IHostEnvironment"/>.</summary>
    public static IHostEnvironment GetEnv(this ApplicationInitializationContext context)
        => context.ServiceProvider.GetRequiredService<IHostEnvironment>();
}
