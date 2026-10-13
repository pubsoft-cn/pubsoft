using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Abp.Core;
using PubSoft.Core;
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
    using var scope = host.Services.CreateScope();
    var generator = scope.ServiceProvider.GetRequiredService<SnowflakeIdGenerator>();
    if (!ReferenceEquals(generator, scope.ServiceProvider.GetRequiredService<SnowflakeIdGenerator>()))
        throw new InvalidOperationException("Snowflake generator registration is not singleton.");

    if (((generator.NextId() >> 7) & 31) != 23)
        throw new InvalidOperationException("Configured Snowflake worker ID was not applied.");
}
finally
{
    await application.ShutdownAsync();
    application.Dispose();
}

Console.WriteLine("Isolated PubSoft.Abp.Core module startup check passed.");

[DependsOn(typeof(PubSoftCoreModule))]
public sealed class ConsumerModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Configure<PubSoftSnowflakeOptions>(options => options.WorkerId = 23);
    }
}