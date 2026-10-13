namespace PubSoft.Contracts;

public static class Guard
{
    public static bool IsValid(params string?[] errors)
        => errors.All(string.IsNullOrEmpty);

    public static bool TryValidate(out string? error, params string?[] errors)
    {
        error = errors.FirstOrDefault(value => !string.IsNullOrEmpty(value));
        return error is null;
    }

    public static string? RequiredAny(string name, params string?[] values)
        => values.All(string.IsNullOrWhiteSpace) ? $"{name} required." : null;

    public static string? Required(string name, string? value)
        => string.IsNullOrWhiteSpace(value) ? $"{name} required." : null;

    public static string? Positive(string name, decimal value)
        => value <= 0 ? $"{name} must be positive." : null;

    public static string? Positive(string name, long value)
        => value <= 0 ? $"{name} must be positive." : null;

    public static string? RequiredAmount(string name, string? value)
        => string.IsNullOrWhiteSpace(value)
            ? $"{name} required."
            : (!decimal.TryParse(value, out var amount) || amount <= 0
                ? $"{name} must be a positive amount."
                : null);
}