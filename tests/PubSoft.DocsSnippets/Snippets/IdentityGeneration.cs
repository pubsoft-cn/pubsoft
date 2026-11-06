// README anchors:
//   src/core/PubSoft.Core/README.md            — SnowflakeIdGenerator
//   src/core/PubSoft.DependencyInjection/README.md — AddPubSoftIdGenerator
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Core;
using PubSoft.DependencyInjection;

namespace PubSoft.DocsSnippets;

/// <summary>Snippets: identity-generation registrations.</summary>
internal static class IdentityGenerationSnippets
{
    // src/core/PubSoft.DependencyInjection/README.md — "AddPubSoftIdGenerator"
    internal static void ExplicitWorkerId(IServiceCollection services, long workerId)
        => services.AddPubSoftIdGenerator(workerId);

    internal static void OptionsDelegate(IServiceCollection services, long w)
        => services.AddPubSoftIdGenerator(o => o.WorkerId = w);

    internal static void FromConfiguration(IServiceCollection services, IConfiguration configuration)
        => services.AddPubSoftIdGenerator(configuration);

    // src/core/PubSoft.Core/README.md — SnowflakeIdGenerator usage
    internal static long NextId(IIdGenerator generator) => generator.NextId();
}
