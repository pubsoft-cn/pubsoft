using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PubSoft.AspNetCore;


public static class DevelopmentOnlyControllerConventionExtensions
{
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
