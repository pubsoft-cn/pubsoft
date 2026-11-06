namespace PubSoft.Authentication;

/// <summary>One claim carried by a principal snapshot.</summary>
/// <param name="Type">Claim type.</param>
/// <param name="Value">Claim value.</param>
/// <param name="ValueType">Value type URI.</param>
/// <param name="Issuer">Issuing authority.</param>
public sealed record PrincipalClaim(string Type, string Value, string ValueType, string Issuer);