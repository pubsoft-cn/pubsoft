using System.Text;

namespace PubSoft.Authorization;


public static class PermissionRequirementDeriver
{
    private static readonly string[] ModifyActionWords =
    [
        "update", "edit", "modify", "set", "change", "save", "bind", "unbind",
        "enable", "disable", "assign", "grant", "revoke", "cancel", "close", "open",
        "start", "stop", "retry", "sync", "import", "export", "submit", "confirm",
        "notify", "send", "publish", "reset", "unlock", "lock"
    ];

    public static PermissionFlags DeriveFlags(string? httpMethod, string? actionName)
    {
        // 缺失/未知 method 一律按最强权限（Custom）处理——缺省归并到最弱 Query 会放行本该受管控的写操作。
        switch (httpMethod?.ToUpperInvariant())
        {
            case "GET":
            case "HEAD":
                return PermissionFlags.Query;
            case "DELETE":
                return PermissionFlags.Delete;
            case "PUT":
            case "PATCH":
                return PermissionFlags.Modify;
            case "POST":
                var name = actionName ?? string.Empty;
                if (ContainsAny(name, "approve", "audit", "review", "reject"))
                    return PermissionFlags.Audit;
                if (ContainsAny(name, "delete", "remove"))
                    return PermissionFlags.Delete;
                if (ContainsAny(name, ModifyActionWords))
                    return PermissionFlags.Modify;
                return PermissionFlags.Create;
            default:
                return PermissionFlags.Custom;
        }
    }

    public static string? ResolveModuleKey(
        string? controllerName,
        string? routeTemplate,
        string? pinnedModuleKey,
        PermissionModuleRegistry? registry,
        IReadOnlyDictionary<string, string>? routeModuleMap = null)
    {
        if (!string.IsNullOrWhiteSpace(pinnedModuleKey))
            return pinnedModuleKey;

        if (!string.IsNullOrWhiteSpace(routeTemplate) && routeModuleMap is not null)
        {
            var routeMatch = routeModuleMap
                .Where(entry => IsRoutePrefixMatch(routeTemplate, entry.Key))
                .OrderByDescending(entry => entry.Key.TrimEnd('/').Length)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(routeMatch.Key))
                return routeMatch.Value;
        }

        if (registry is null || string.IsNullOrWhiteSpace(controllerName))
            return null;

        var snakeCaseController = ToSnakeCase(controllerName);
        if (snakeCaseController.Length == 0)
            return null;

        var suffix = "_" + snakeCaseController;
        var matches = registry.AllModules
            .Where(moduleKey => moduleKey.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                || moduleKey.Equals(snakeCaseController, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var result = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character))
            {
                if (index > 0 && (char.IsLower(name[index - 1])
                    || (index + 1 < name.Length && char.IsLower(name[index + 1]))))
                    result.Append('_');
                result.Append(char.ToLowerInvariant(character));
            }
            else
            {
                result.Append(char.ToLowerInvariant(character));
            }
        }

        return result.ToString();
    }

    private static bool IsRoutePrefixMatch(string routeTemplate, string configuredPrefix)
    {
        var prefix = configuredPrefix.TrimEnd('/');
        if (prefix.Length == 0)
            return false;
        return routeTemplate.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || routeTemplate.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
