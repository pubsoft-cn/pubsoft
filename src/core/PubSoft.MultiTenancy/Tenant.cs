using PubSoft.Domain;

namespace PubSoft.MultiTenancy;

/// <summary>Tenant aggregate.</summary>
[PubSoft.Core.BoundedCost("data object - timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public sealed class Tenant : Entity<long>
{

    /// <summary>Creates a tenant; code/name are required and bounded.</summary>
    public Tenant(long id, string code, string name, string? displayName = null, string? description = null)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "Tenant ID must be positive.");
        Id = id;
        Code = NormalizeRequired(code, nameof(code), 64);
        Name = NormalizeRequired(name, nameof(name), 100);
        DisplayName = NormalizeBounded(displayName, nameof(displayName), 100) ?? Name;
        Description = NormalizeBounded(description, nameof(description), 200);
        CreationTime = DateTime.UtcNow;
    }

    private Tenant() { }
    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;
    /// <summary>Stable code used by the app.</summary>
    public string Code { get; private set; } = string.Empty;
    /// <summary>UI-facing name (falls back to Name).</summary>
    public string DisplayName { get; private set; } = string.Empty;
    /// <summary>Disabled tenants are treated as nonexistent.</summary>
    public bool IsEnabled { get; private set; } = true;
    /// <summary>Optional description.</summary>
    public string? Description { get; private set; }
    /// <summary>When the tenant was created.</summary>
    public DateTime CreationTime { get; private set; }

    /// <summary>Updates mutable fields.</summary>
    public void Update(string name, string? displayName, string? description)
    {
        Name = NormalizeRequired(name, nameof(name), 100);
        DisplayName = NormalizeBounded(displayName, nameof(displayName), 100) ?? Name;
        Description = NormalizeBounded(description, nameof(description), 200);
    }

    /// <summary>Toggles the tenant.</summary>
    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    private static string NormalizeRequired(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeBounded(string? value, string parameterName, int maximumLength)
    {
        var normalized = NormalizeOptional(value);
        if (normalized?.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
        return normalized;
    }
}