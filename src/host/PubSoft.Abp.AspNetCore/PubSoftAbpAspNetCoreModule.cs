using Volo.Abp.Modularity;

namespace PubSoft.Abp.AspNetCore;

public sealed class PubSoftAbpAspNetCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddPubSoftAbpAspNetCore();
    }
}