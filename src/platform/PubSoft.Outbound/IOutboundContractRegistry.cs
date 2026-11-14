using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

/// <summary>Contract metadata store: operation-id and request-type lookups.</summary>
public interface IOutboundContractRegistry
{
    /// <summary>All registered contracts.</summary>
    IReadOnlyList<ApiContractMetadata> Contracts { get; }
    /// <summary>Finds a contract by operation id, or null.</summary>
    ApiContractMetadata? FindByOperationId(string operationId);
    /// <summary>Resolves contract metadata for a request type (throws when unregistered).</summary>
    ApiContractMetadata GetMetadata(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type requestType);

    /// <summary>Discovers contract types in an assembly.</summary>
    [RequiresUnreferencedCode("Assembly scanning discovers contract types that may be trimmed.")]
    void ScanAssembly(Assembly assembly);
}

/// <summary>Thread-safe contract registry.</summary>
public sealed class OutboundContractRegistry : IOutboundContractRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ApiContractMetadata> _byOperationId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, ApiContractMetadata> _byRequestType = [];
    private readonly HashSet<Type> _scannedTypes = [];
    private readonly INamingPolicy _namingPolicy;
    private IReadOnlyList<ApiContractMetadata> _contracts = Array.AsReadOnly(Array.Empty<ApiContractMetadata>());

    /// <summary>Creates a registry with camelCase naming.</summary>
    public OutboundContractRegistry() : this(CamelCaseNamingPolicy.Instance) { }

    /// <summary>Creates a registry with a naming policy.</summary>
    public OutboundContractRegistry(INamingPolicy namingPolicy)
        => _namingPolicy = namingPolicy ?? throw new ArgumentNullException(nameof(namingPolicy));

    /// <inheritdoc />
    public IReadOnlyList<ApiContractMetadata> Contracts
    {
        get
        {
            lock (_gate)
                return _contracts;
        }
    }

    /// <inheritdoc />
    public ApiContractMetadata? FindByOperationId(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        lock (_gate)
            return _byOperationId.GetValueOrDefault(operationId);
    }

    /// <inheritdoc />
    public ApiContractMetadata GetMetadata(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        lock (_gate)
        {
            if (_byRequestType.TryGetValue(requestType, out var metadata))
                return metadata;

            metadata = OutboundContractMetadataResolver.Resolve(requestType, _namingPolicy);
            _byRequestType.Add(requestType, metadata);
            return metadata;
        }
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Assembly scanning discovers contract types that may be trimmed.")]
    [PubSoft.Core.BoundedCost("assembly scan is opt-in and runs at registration, not per request")]
    public void ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Type[] exported;
        try
        {
            exported = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            exported = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
        var discovered = exported
            .Where(type => !type.IsAbstract && !type.IsInterface && type.IsDefined(typeof(ApiOperationAttribute), inherit: false))
            .Select(type => OutboundContractMetadataResolver.Resolve(type, _namingPolicy))
            .ToArray();

        lock (_gate)
        {
            var staged = new Dictionary<string, ApiContractMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var metadata in discovered)
            {
                if (_scannedTypes.Contains(metadata.RequestType))
                    continue;

                foreach (var key in new[] { metadata.OperationId }.Concat(metadata.Aliases))
                {
                    if (_byOperationId.ContainsKey(key) || !staged.TryAdd(key, metadata))
                        throw new InvalidOperationException($"Duplicate outbound operation ID or alias '{key}'.");
                }
            }

            foreach (var item in staged)
                _byOperationId.Add(item.Key, item.Value);
            foreach (var metadata in discovered)
            {
                _byRequestType.TryAdd(metadata.RequestType, metadata);
                _scannedTypes.Add(metadata.RequestType);
            }

            var contracts = _byRequestType.Values.ToArray();
            Array.Sort(contracts, static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.OperationId, right.OperationId));
            _contracts = Array.AsReadOnly(contracts);
        }
    }
}
