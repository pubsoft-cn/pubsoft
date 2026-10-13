using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PubSoft.Resilience;
using PubSoft.Scheduling;

namespace PubSoft.Scheduling.Http;

public static class HttpExecutorServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftHttpExecutors(
        this IServiceCollection services,
        Action<HttpExecutorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<HttpExecutorOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(options => options.IsValid(), "HTTP executor options are invalid.")
            .ValidateOnStart();
        services.AddHttpClient(HttpExecutionSupport.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<HttpExecutorOptions>>().Value;
            return new CircuitBreaker
            {
                Options = new CircuitBreakerOptions
                {
                    FailureThreshold = options.CircuitFailureThreshold,
                    OpenTimeout = options.CircuitOpenTimeout
                }
            };
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobExecutor, ProbeExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobExecutor, ResilientExecutor>());
        return services;
    }
}