namespace PubSoft.Contracts;

/// <summary>Contract that validates itself.</summary>
public interface IContractValidator
{
    /// <summary>Validates; on failure returns false with the first error.</summary>
    bool Validate(out string? error);

    /// <summary>Validates, discarding the error detail.</summary>
    bool Validate() => Validate(out _);
}