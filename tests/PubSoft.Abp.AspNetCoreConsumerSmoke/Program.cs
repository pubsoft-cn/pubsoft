using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Abp.AspNetCore;
using PubSoft.Authentication;
using Volo.Abp;
using Volo.Abp.Modularity;

var builder = Host.CreateApplicationBuilder();
var application = await AbpApplicationFactory.CreateAsync<ConsumerModule>(builder.Services);
builder.Services.AddSingleton<IAbpApplicationWithExternalServiceProvider>(application);
builder.Services.AddSingleton(application);

using var host = builder.Build();
await application.InitializeAsync(host.Services);
try
{
    using var firstScope = host.Services.CreateScope();
    using var secondScope = host.Services.CreateScope();
    var first = firstScope.ServiceProvider.GetRequiredService<IPrincipalSnapshotAccessor>();
    var second = secondScope.ServiceProvider.GetRequiredService<IPrincipalSnapshotAccessor>();
    if (ReferenceEquals(first, second) || first.GetCurrent() is not null || second.GetCurrent() is not null)
        throw new InvalidOperationException("Principal snapshot accessor scope or empty-request behavior is invalid.");
}
finally
{
    await application.ShutdownAsync();
    application.Dispose();
}

Console.WriteLine("Isolated PubSoft.Abp.AspNetCore module startup check passed.");

[DependsOn(typeof(PubSoftAbpAspNetCoreModule))]
public sealed class ConsumerModule : AbpModule;