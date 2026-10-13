namespace PubSoft.Contracts;

public interface IContractValidator
{
    bool Validate(out string? error);

    bool Validate() => Validate(out _);
}