using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PubSoft.Core;

namespace PubSoft.Abp.Core;

public static class PubSoftCoreServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PubSoftSnowflakeOptions>()
            .Validate(options => options.WorkerId is >= 0 and <= 31,
                "A unique Snowflake worker ID from 0 through 31 must be configured by the host.");
        services.TryAddSingleton(serviceProvider =>
        {
            var workerId = serviceProvider.GetRequiredService<IOptions<PubSoftSnowflakeOptions>>().Value.WorkerId;
            return new SnowflakeIdGenerator(workerId!.Value);
        });

        return services;
    }
}