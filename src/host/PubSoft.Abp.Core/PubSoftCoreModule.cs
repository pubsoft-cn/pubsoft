using Volo.Abp.Modularity;

namespace PubSoft.Abp.Core;

public sealed class PubSoftCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddPubSoftCore();
    }
}