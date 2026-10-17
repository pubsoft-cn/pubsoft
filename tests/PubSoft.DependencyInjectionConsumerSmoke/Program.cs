using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.DependencyInjection;

var services = new ServiceCollection();
services.AddConventionalServices(Assembly.GetExecutingAssembly());
using var sp = services.BuildServiceProvider();
if (sp.GetService<ISmokeService>() is not SmokeService || sp.GetService<SmokeService>() is null)
    throw new InvalidOperationException("Conventional registration consumer check failed.");
if (sp.GetService<UnmarkedService>() is not null)
    throw new InvalidOperationException("Unmarked service leaked into DI.");

Console.WriteLine("Isolated PubSoft.DependencyInjection consumer smoke check passed.");

public interface ISmokeService { }
public sealed class SmokeService : ISmokeService, ITransientDependency { }
public sealed class UnmarkedService { }
