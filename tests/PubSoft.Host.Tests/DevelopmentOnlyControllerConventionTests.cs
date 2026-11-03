using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DevelopmentOnlyControllerConventionTests
{
    [Fact]
    public void ProductionEnvironmentRemovesOnlyDevelopmentOnlyControllers()
    {
        var application = CreateApplicationModel();
        var convention = new DevelopmentOnlyControllerConvention(new TestHostEnvironment(Environments.Production));

        convention.Apply(application);

        Assert.Collection(application.Controllers,
            controller => Assert.Equal(typeof(RegularController), controller.ControllerType));
    }

    [Fact]
    public void DevelopmentEnvironmentKeepsDevelopmentOnlyControllers()
    {
        var application = CreateApplicationModel();
        var convention = new DevelopmentOnlyControllerConvention(new TestHostEnvironment(Environments.Development));

        convention.Apply(application);

        Assert.Equal(2, application.Controllers.Count);
    }

    [Fact]
    public void MvcRegistrationExtensionAddsTheConventionExplicitly()
    {
        var services = new ServiceCollection();
        services.AddControllers()
            .AddPubSoftDevelopmentOnlyControllerConvention(new TestHostEnvironment(Environments.Production));
        using var provider = services.BuildServiceProvider();

        var conventions = provider.GetRequiredService<IOptions<MvcOptions>>().Value.Conventions;

        Assert.Contains(conventions, convention => convention is DevelopmentOnlyControllerConvention);
    }

    private static ApplicationModel CreateApplicationModel()
    {
        var application = new ApplicationModel();
        application.Controllers.Add(CreateControllerModel(typeof(DevelopmentController)));
        application.Controllers.Add(CreateControllerModel(typeof(RegularController)));
        return application;
    }

    private static ControllerModel CreateControllerModel(Type controllerType)
    {
        var typeInfo = controllerType.GetTypeInfo();
        return new ControllerModel(typeInfo, typeInfo.GetCustomAttributes(inherit: true).Cast<object>().ToArray());
    }

    [DevelopmentOnly]
    private sealed class DevelopmentController : ControllerBase;

    private sealed class RegularController : ControllerBase;

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "PubSoft.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
