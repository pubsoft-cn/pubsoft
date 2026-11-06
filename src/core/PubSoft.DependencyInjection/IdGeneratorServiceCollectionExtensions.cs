using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Core;

namespace PubSoft.DependencyInjection;

/// <summary>Id generator registration.</summary>
public static class IdGeneratorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SnowflakeIdGenerator"/> as the shared <see cref="IIdGenerator"/>.
    /// <paramref name="workerId"/> (0–<see cref="SnowflakeIdGenerator.MaxWorkerId"/>) must be
    /// unique per replica - two generators sharing a worker id can emit colliding ids.
    /// A host-provided <see cref="IIdGenerator"/> registered earlier is honored instead.
    /// </summary>
    public static IServiceCollection AddPubSoftIdGenerator(this IServiceCollection services, long workerId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateWorkerId(workerId);
        services.TryAddSingleton<SnowflakeIdGenerator>(_ => new SnowflakeIdGenerator(workerId));
        services.TryAddSingleton<IIdGenerator>(provider => provider.GetRequiredService<SnowflakeIdGenerator>());
        return services;
    }

    /// <summary>Registers the generator from an options delegate - hosts wire their own config source.</summary>
    public static IServiceCollection AddPubSoftIdGenerator(
        this IServiceCollection services,
        Action<IdGeneratorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new IdGeneratorOptions();
        configure(options);
        return services.AddPubSoftIdGenerator(options.WorkerId);
    }

    /// <summary>
    /// Binds <c>PubSoft:IdGenerator:WorkerId</c> from configuration. Fails closed when the key
    /// is missing or out of range - a guessed worker id collides silently across replicas.
    /// </summary>
    public static IServiceCollection AddPubSoftIdGenerator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration[$"{IdGeneratorOptions.SectionName}:WorkerId"];
        if (!long.TryParse(raw, out var workerId))
        {
            throw new InvalidOperationException(
                $"{IdGeneratorOptions.SectionName}:WorkerId is required (0–{SnowflakeIdGenerator.MaxWorkerId}, unique per replica).");
        }
        return services.AddPubSoftIdGenerator(workerId);
    }

    private static void ValidateWorkerId(long workerId)
    {
        if (workerId < 0 || workerId > SnowflakeIdGenerator.MaxWorkerId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerId), $"Worker id must be between 0 and {SnowflakeIdGenerator.MaxWorkerId}.");
        }
    }
}
