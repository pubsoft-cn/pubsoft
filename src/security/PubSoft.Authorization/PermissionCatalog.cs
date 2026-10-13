namespace PubSoft.Authorization;

[Flags]
public enum PermissionFlags : byte
{
    None = 0,
    Query = 1,
    Create = 2,
    Modify = 4,
    Delete = 8,
    Audit = 16,
    Custom = 32,
    All = Query | Create | Modify | Delete | Audit | Custom
}

public sealed record PermissionDefinition
{
    public string ClientCode { get; init; } = string.Empty;
    public required string ModuleKey { get; init; }
    public required PermissionFlags Flags { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> ApiPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FrontendRoutes { get; init; } = Array.Empty<string>();
    public bool IsExtPermission { get; init; }
    public uint ExtFlag { get; init; }
}

public interface IPermissionDefinitionProvider
{
    IReadOnlyList<PermissionDefinition> GetDefinitions();
}

public sealed class PermissionDefinitionRegistry
{
    private readonly IReadOnlyList<PermissionDefinition> _definitions;

    public PermissionDefinitionRegistry(IEnumerable<IPermissionDefinitionProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var definitions = new List<PermissionDefinition>();
        var keys = new HashSet<(string ClientCode, string ModuleKey, PermissionFlags Flags, uint ExtFlag)>();
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var providedDefinitions = provider.GetDefinitions()
                ?? throw new InvalidOperationException("Permission providers must return a definition list.");
            foreach (var definition in providedDefinitions)
            {
                ArgumentNullException.ThrowIfNull(definition);
                var snapshot = ValidateAndSnapshot(definition);
                if (!keys.Add((snapshot.ClientCode, snapshot.ModuleKey, snapshot.Flags, snapshot.ExtFlag)))
                    throw new ArgumentException(
                        $"Duplicate permission definition for module '{snapshot.ModuleKey}' and flags '{snapshot.Flags}'.",
                        nameof(providers));
                definitions.Add(snapshot);
            }
        }

        _definitions = Array.AsReadOnly(definitions.ToArray());
    }

    public IReadOnlyList<PermissionDefinition> AllDefinitions => _definitions;

    public IReadOnlyList<PermissionDefinition> GetByModule(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        return Array.AsReadOnly(_definitions.Where(definition => definition.ModuleKey == moduleKey).ToArray());
    }

    public IReadOnlyList<PermissionDefinition> GetByModule(string moduleKey, string clientCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        ArgumentNullException.ThrowIfNull(clientCode);
        if (clientCode.Length > 64 || clientCode != clientCode.Trim())
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(clientCode));
        return Array.AsReadOnly(_definitions
            .Where(definition => definition.ModuleKey == moduleKey && definition.ClientCode == clientCode)
            .ToArray());
    }

    public IReadOnlyList<PermissionDefinition> GetByFrontendRoute(string route)
    {
        var normalizedRoute = ValidatePath(route, nameof(route), allowTemplates: false);
        return Array.AsReadOnly(_definitions
            .Where(definition => definition.FrontendRoutes.Any(pattern => PathMatches(pattern, normalizedRoute)))
            .ToArray());
    }

    public IReadOnlyList<PermissionDefinition> GetByApiPath(string httpMethod, string path)
    {
        ValidateHttpMethod(httpMethod, nameof(httpMethod));
        var normalizedPath = ValidatePath(path, nameof(path), allowTemplates: false);
        return Array.AsReadOnly(_definitions
            .Where(definition => definition.ApiPaths.Any(pattern => ApiPathMatches(pattern, httpMethod, normalizedPath)))
            .ToArray());
    }

    private static PermissionDefinition ValidateAndSnapshot(PermissionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ModuleKey) || definition.ModuleKey != definition.ModuleKey.Trim())
            throw new ArgumentException("Permission module keys must be non-empty and have no surrounding whitespace.");
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            throw new ArgumentException("Permission display names must be non-empty.");
        if (definition.ClientCode is null || definition.ClientCode.Length > 64
            || definition.ClientCode != definition.ClientCode.Trim())
            throw new ArgumentException("Permission client codes must be trimmed and at most 64 characters.");
        if (definition.Flags == PermissionFlags.None || (definition.Flags & ~PermissionFlags.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(definition), "Permission flags must contain only defined, non-zero bits.");
        if (definition.IsExtPermission != (definition.ExtFlag != 0))
            throw new ArgumentException("Extended permissions require a non-zero extension flag, and standard permissions must not set one.");

        var apiPaths = definition.ApiPaths ?? throw new ArgumentException("API paths cannot be null.", nameof(definition));
        var frontendRoutes = definition.FrontendRoutes ?? throw new ArgumentException("Frontend routes cannot be null.", nameof(definition));
        var normalizedApiPaths = apiPaths.Select(ValidateApiPattern).ToArray();
        var normalizedRoutes = frontendRoutes.Select(route => ValidatePath(route, nameof(definition), allowTemplates: true)).ToArray();

        if (normalizedApiPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedApiPaths.Length)
            throw new ArgumentException("Permission API paths must be unique.", nameof(definition));
        if (normalizedRoutes.Distinct(StringComparer.Ordinal).Count() != normalizedRoutes.Length)
            throw new ArgumentException("Permission frontend routes must be unique.", nameof(definition));

        return definition with
        {
            ApiPaths = Array.AsReadOnly(normalizedApiPaths),
            FrontendRoutes = Array.AsReadOnly(normalizedRoutes)
        };
    }

    private static string ValidateApiPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var separator = pattern.IndexOf(' ');
        if (separator <= 0 || separator == pattern.Length - 1 || pattern.IndexOf(' ', separator + 1) >= 0)
            throw new ArgumentException("API paths must use the 'METHOD /path' format.", nameof(pattern));

        var method = pattern[..separator];
        ValidateHttpMethod(method, nameof(pattern));
        var path = ValidatePath(pattern[(separator + 1)..], nameof(pattern), allowTemplates: true);
        return $"{method.ToUpperInvariant()} {path}";
    }

    private static void ValidateHttpMethod(string method, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method, parameterName);
        if (method.Any(character => !char.IsAsciiLetter(character)))
            throw new ArgumentException("HTTP methods must contain ASCII letters only.", parameterName);
    }

    private static string ValidatePath(string path, string parameterName, bool allowTemplates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (path[0] != '/' || path.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
            || path.Contains('?') || path.Contains('#'))
            throw new ArgumentException("Paths must be absolute path-only values without whitespace, query, or fragment.", parameterName);

        var normalized = path.Length > 1 ? path.TrimEnd('/') : path;
        var segments = GetSegments(normalized);
        if (segments.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Paths cannot contain empty segments.", parameterName);
        if (allowTemplates && segments.Any(segment => !IsValidTemplateSegment(segment)))
            throw new ArgumentException("Route parameters must occupy a full segment and use :name or {name} syntax.", parameterName);
        if (!allowTemplates && segments.Any(segment => segment.Contains('{') || segment.Contains('}')))
            throw new ArgumentException("Lookup paths cannot contain template braces.", parameterName);
        return normalized;
    }

    private static bool IsValidTemplateSegment(string segment)
    {
        if (segment.Contains('{') || segment.Contains('}'))
            return segment.Length > 2 && segment[0] == '{' && segment[^1] == '}'
                && IsParameterName(segment[1..^1]);
        if (segment.StartsWith(':'))
            return IsParameterName(segment[1..]);
        return true;
    }

    private static bool IsParameterName(string name) => name.Length > 0
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool ApiPathMatches(string pattern, string httpMethod, string path)
    {
        var separator = pattern.IndexOf(' ');
        return pattern[..separator].Equals(httpMethod, StringComparison.OrdinalIgnoreCase)
            && PathMatches(pattern[(separator + 1)..], path);
    }

    private static bool PathMatches(string pattern, string path)
    {
        var patternSegments = GetSegments(pattern);
        var pathSegments = GetSegments(path);
        if (patternSegments.Length != pathSegments.Length)
            return false;

        for (var index = 0; index < patternSegments.Length; index++)
        {
            var patternSegment = patternSegments[index];
            var isParameter = patternSegment.StartsWith(':')
                || (patternSegment.StartsWith('{') && patternSegment.EndsWith('}'));
            if (!isParameter && !patternSegment.Equals(pathSegments[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string[] GetSegments(string path) => path == "/"
        ? Array.Empty<string>()
        : path[1..].Split('/');
}