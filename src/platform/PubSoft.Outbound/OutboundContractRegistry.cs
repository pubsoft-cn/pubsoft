using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

public interface IOutboundContractRegistry
{
    IReadOnlyList<ApiContractMetadata> Contracts { get; }
    ApiContractMetadata? FindByOperationId(string operationId);
    ApiContractMetadata GetMetadata(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type requestType);

    [RequiresUnreferencedCode("Assembly scanning discovers contract types that may be trimmed.")]
    void ScanAssembly(Assembly assembly);
}

public sealed class OutboundContractRegistry : IOutboundContractRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ApiContractMetadata> _byOperationId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, ApiContractMetadata> _byRequestType = [];
    private readonly HashSet<Type> _scannedTypes = [];
    private readonly INamingPolicy _namingPolicy;
    private IReadOnlyList<ApiContractMetadata> _contracts = Array.AsReadOnly(Array.Empty<ApiContractMetadata>());

    public OutboundContractRegistry() : this(CamelCaseNamingPolicy.Instance) { }

    public OutboundContractRegistry(INamingPolicy namingPolicy)
        => _namingPolicy = namingPolicy ?? throw new ArgumentNullException(nameof(namingPolicy));

    public IReadOnlyList<ApiContractMetadata> Contracts
    {
        get
        {
            lock (_gate)
                return _contracts;
        }
    }

    public ApiContractMetadata? FindByOperationId(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        lock (_gate)
            return _byOperationId.GetValueOrDefault(operationId);
    }

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

    [RequiresUnreferencedCode("Assembly scanning discovers contract types that may be trimmed.")]
    public void ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var discovered = assembly.GetExportedTypes()
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