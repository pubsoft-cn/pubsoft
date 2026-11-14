using System.Text.Json;

namespace PubSoft.Core;

/// <summary>
/// Allow-list matcher over a JSON string-array column (e.g. <c>["app-1","app-2"]</c>), the
/// storage shape credential registries already use. Malformed JSON is a data defect and throws
/// at construction. Empty-list behavior is explicit via <see cref="GateEmptyPolicy"/> (default
/// deny). Matching is ordinal.
/// </summary>
public sealed class JsonAllowList
{
    private readonly HashSet<string>? _entries;
    private readonly GateEmptyPolicy _emptyPolicy;

    /// <summary>Creates the matcher from a JSON string array; null/blank means empty.</summary>
    /// <exception cref="ArgumentException">The JSON is malformed or not an array of strings.</exception>
    public JsonAllowList(string? json, GateEmptyPolicy emptyPolicy = GateEmptyPolicy.Deny)
    {
        _emptyPolicy = emptyPolicy;
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("The JSON allow-list column must be a string array.", nameof(json));
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
                    throw new ArgumentException("The JSON allow-list column must contain non-empty strings.", nameof(json));
                (_entries ??= new HashSet<string>(StringComparer.Ordinal)).Add(element.GetString()!);
            }
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The JSON allow-list column is malformed.", nameof(json), ex);
        }
    }

    /// <summary>Whether the list holds no entries.</summary>
    public bool IsEmpty => _entries is null or { Count: 0 };

    /// <summary>Admits <paramref name="value"/> on ordinal match, or when the list is empty under <see cref="GateEmptyPolicy.Allow"/>.</summary>
    public bool IsAllowed(string? value)
        => IsEmpty
            ? _emptyPolicy == GateEmptyPolicy.Allow
            : value is not null && _entries!.Contains(value);
}
