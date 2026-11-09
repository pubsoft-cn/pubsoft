using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PubSoft.AspNetCore;


/// <summary>Convention stripping designated controllers outside Development.</summary>
public sealed class DevelopmentOnlyControllerConvention : IApplicationModelConvention
{
    private readonly bool _isDevelopment;

    /// <summary>Creates the convention.</summary>
    public DevelopmentOnlyControllerConvention(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _isDevelopment = environment.IsDevelopment();
    }

    /// <inheritdoc />
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (_isDevelopment)
            return;

        for (var index = application.Controllers.Count - 1; index >= 0; index--)
        {
            if (application.Controllers[index].Attributes.OfType<DevelopmentOnlyAttribute>().Any())
                application.Controllers.RemoveAt(index);
        }
    }
}
