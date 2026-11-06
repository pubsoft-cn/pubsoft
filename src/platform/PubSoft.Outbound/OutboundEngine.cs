using System.Diagnostics.CodeAnalysis;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

/// <summary>Routes contract calls to the single matching outbound provider.</summary>
public sealed class OutboundEngine(
    IEnumerable<IOutboundProvider> providers,
    IOutboundContractRegistry registry) : IOutboundEngine
{
    private readonly IReadOnlyList<IOutboundProvider> _providers = providers?.ToArray()
        ?? throw new ArgumentNullException(nameof(providers));
    private readonly IOutboundContractRegistry _registry = registry
        ?? throw new ArgumentNullException(nameof(registry));

    /// <inheritdoc />
    [RequiresUnreferencedCode("Contract mapping uses reflection over request members; preserve contract types under trimming.")]
    public async Task<TResponse> ExecuteAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TRequest, TResponse>(
        TRequest request,
        string profileId,
        long tenantId,
        CancellationToken cancellationToken = default)
        where TRequest : IApiRequest<TResponse>
        where TResponse : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = _registry.GetMetadata(typeof(TRequest));
        var matchingProviders = _providers.Where(provider => provider.CanExecute(metadata.OperationId)).Take(2).ToArray();
        if (matchingProviders.Length == 0)
            throw new InvalidOperationException($"No integration provider can execute '{metadata.OperationId}'.");
        if (matchingProviders.Length > 1)
            throw new InvalidOperationException($"More than one integration provider can execute '{metadata.OperationId}'.");

        if (request is IContractNormalizer normalizable)
            normalizable.Normalize();

        var requiredError = RequiredFieldValidator.Validate(request);
        if (requiredError is not null)
            throw new InvalidOperationException(requiredError);

        if (request is IContractValidator validator)
        {
            var isValid = validator.Validate(out var validationError);
            if (!isValid || validationError is not null)
                throw new InvalidOperationException(
                    $"Validation failed for '{typeof(TRequest).Name}': {validationError ?? "contract validation failed"}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var context = new OutboundProviderContext
        {
            ProfileId = profileId,
            TenantId = tenantId,
            OperationId = metadata.OperationId
        };
        var result = await matchingProviders[0].ExecuteAsync(
            metadata.OperationId, request, context, cancellationToken).ConfigureAwait(false);
        if (result is TResponse typed)
            return typed;

        var actualType = result?.GetType().Name ?? "null";
        throw new InvalidOperationException($"Provider returned {actualType}; expected {typeof(TResponse).Name}.");
    }
}