// README anchors: Scheduling + Caching.Redis — distributed locking + health check
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using PubSoft.Caching.Redis;
using PubSoft.Scheduling;

namespace PubSoft.DocsSnippets;

/// <summary>Snippets: scheduler locking + redis registration + health.</summary>
internal static class SchedulingAndCachingSnippets
{
    // src/platform/PubSoft.Scheduling/README.md — DistributedLocking
    internal static void Scheduler(IServiceCollection services)
        => services.AddPubSoftScheduler(o =>
        {
            o.DistributedLocking = true;
            o.DistributedLockLease = TimeSpan.FromMinutes(5);
        });

    // src/platform/PubSoft.Caching.Redis/README.md — health check
    internal static void Health(IHealthChecksBuilder builder)
        => builder.AddPubSoftRedisCheck();
}
