using PubSoft.Core;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

public static class OutboundContractMetadataResolver
{
    [PubSoft.Core.BoundedCost("assembly scan runs once at registration")]
    public static ApiContractMetadata Resolve(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type requestType)
        => Resolve(requestType, null);

    public static ApiContractMetadata Resolve(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type requestType,
        INamingPolicy? namingPolicy)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        var operation = requestType.GetCustomAttribute<ApiOperationAttribute>()
            ?? throw new InvalidOperationException($"Missing ApiOperation on {requestType.Name}.");
        var operationId = operation.OperationId;
        if (!IsValidOperationId(operationId))
            throw new InvalidOperationException($"Invalid operation ID on {requestType.Name}.");

        var responseTypes = requestType.GetInterfaces()
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IApiRequest<>))
            .Select(type => type.GetGenericArguments()[0])
            .Distinct()
            .ToArray();
        if (responseTypes.Length != 1)
            throw new InvalidOperationException($"Contract {requestType.Name} must implement exactly one IApiRequest<TResponse>.");

        var segments = operationId.Split('.');
        var routePrefix = segments.Length > 1 ? string.Join('/', segments[..^1]) : segments[0];
        var routeAction = segments[^1];
        var firstSeparator = operationId.IndexOf('.');
        var routeSuffix = firstSeparator >= 0
            ? operationId[(firstSeparator + 1)..].Replace('.', '/')
            : operationId;
        var aliases = operation.Aliases?.ToArray() ?? [];
        if (aliases.Any(alias => !IsValidOperationId(alias)))
            throw new InvalidOperationException($"Aliases on {requestType.Name} must be valid operation IDs.");

        return new ApiContractMetadata
        {
            OperationId = operationId,
            RoutePrefix = routePrefix,
            RouteAction = routeAction,
            RouteSuffix = routeSuffix,
            Verb = operation.Verb,
            RequestType = requestType,
            ResponseType = responseTypes[0],
            Aliases = Array.AsReadOnly(aliases),
            NamingPolicy = namingPolicy ?? CamelCaseNamingPolicy.Instance
        };
    }

    public static string RoutePath(ApiContractMetadata metadata, string profileId = "default")
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var prefix = string.Join('/', metadata.RoutePrefix.Split('/').Select(Uri.EscapeDataString));
        return $"/outbound/v1/{prefix}/{Uri.EscapeDataString(profileId)}/{Uri.EscapeDataString(metadata.RouteAction)}";
    }

    public static string RoutePath<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TRequest>(string profileId = "default")
        where TRequest : IApiRequest
        => RoutePath(Resolve(typeof(TRequest)), profileId);

    private static bool IsValidOperationId(string? value)
        => !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), System.StringComparison.Ordinal)
            && value.Split('.').All(segment => segment.Length > 0
                && !segment.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)
                    || character is '/' or '\\' or '?' or '#'));
}