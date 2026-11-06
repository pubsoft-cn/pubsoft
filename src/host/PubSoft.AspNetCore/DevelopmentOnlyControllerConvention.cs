using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PubSoft.AspNetCore;


public sealed class DevelopmentOnlyControllerConvention : IApplicationModelConvention
{
    private readonly bool _isDevelopment;

    public DevelopmentOnlyControllerConvention(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _isDevelopment = environment.IsDevelopment();
    }

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
