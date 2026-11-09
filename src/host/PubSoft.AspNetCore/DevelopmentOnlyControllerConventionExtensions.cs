using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PubSoft.AspNetCore;


/// <summary>Registration for the development-only controller convention.</summary>
public static class DevelopmentOnlyControllerConventionExtensions
{
    /// <summary>Registers the convention.</summary>
    public static IMvcBuilder AddPubSoftDevelopmentOnlyControllerConvention(
        this IMvcBuilder builder,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(environment);

        builder.AddMvcOptions(options =>
            options.Conventions.Add(new DevelopmentOnlyControllerConvention(environment)));
        return builder;
    }
}
