namespace PubSoft.Contracts;

/// <summary>Contract that normalizes itself before validation/serialization.</summary>
public interface IContractNormalizer
{
    /// <summary>Normalizes the contract in place.</summary>
    void Normalize();
}