namespace PubSoft.Authentication;

public sealed record PrincipalClaim(string Type, string Value, string ValueType, string Issuer);