namespace PubSoft.Authentication.Jwt;

/// <summary>
/// Options for <see cref="ConfigJwtIssuerKeyRing"/>. Keys map key ids to RSA private keys in PEM
/// form. Every configured key is published through JWKS; rollover is an operator concern (add the
/// new key, flip <see cref="ActiveKeyId"/>, remove the old entry once its tokens have expired).
/// </summary>
public sealed class JwtIssuerKeyRingOptions
{
    /// <summary>Default configuration section bound by the DI registration extension.</summary>
    public const string SectionName = "Jwt:IssuerKeys";

    /// <summary>Identifier of the key used to sign new tokens. Must exist in <see cref="Keys"/>.</summary>
    public required string ActiveKeyId { get; set; }

    /// <summary>Key id -> RSA private key PEM (at least 2048 bits).</summary>
    public required IReadOnlyDictionary<string, string> Keys { get; set; }
}
