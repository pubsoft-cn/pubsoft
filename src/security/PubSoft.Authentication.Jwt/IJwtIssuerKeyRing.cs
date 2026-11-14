namespace PubSoft.Authentication.Jwt;

/// <summary>
/// Issuer-side RSA signing key ring: one active key signs new tokens while all published keys
/// stay visible through JWKS so consumers keep validating during rollover. Key generation,
/// storage and scheduled rotation are host operations -- the ring only exposes the configured set.
/// </summary>
public interface IJwtIssuerKeyRing : IDisposable
{
    /// <summary>Key used to sign newly issued tokens.</summary>
    RsaSigningKeyMaterial ActiveKey { get; }

    /// <summary>Keys whose public material is published via JWKS (the active key plus retiring keys).</summary>
    IReadOnlyCollection<RsaSigningKeyMaterial> PublishedKeys { get; }
}
