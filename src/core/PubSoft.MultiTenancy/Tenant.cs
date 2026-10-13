using PubSoft.Domain;

namespace PubSoft.MultiTenancy;

public sealed class Tenant : Entity<long>
{
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; } = true;
    public string? Description { get; private set; }
    public DateTime CreationTime { get; private set; }

    private Tenant() { }

    public Tenant(long id, string code, string name, string? displayName = null, string? description = null)
    {
        Id = id;
        Code = NormalizeRequired(code, nameof(code), 64);
        Name = NormalizeRequired(name, nameof(name), 100);
        DisplayName = NormalizeBounded(displayName, nameof(displayName), 100) ?? Name;
        Description = NormalizeBounded(description, nameof(description), 200);
        CreationTime = DateTime.UtcNow;
    }

    public void Update(string name, string? displayName, string? description)
    {
        Name = NormalizeRequired(name, nameof(name), 100);
        DisplayName = NormalizeBounded(displayName, nameof(displayName), 100) ?? Name;
        Description = NormalizeBounded(description, nameof(description), 200);
    }

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