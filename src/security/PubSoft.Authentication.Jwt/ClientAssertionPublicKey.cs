using System.Security.Cryptography;

namespace PubSoft.Authentication.Jwt;

/// <summary>
/// A client's public assertion-verification key - immutable public parameters, no lifetime
/// contract. Callers receive a private copy of the parameter arrays and materialize (and
/// dispose) their own key objects per verification.
/// </summary>
/// <param name="KeyId">The <c>kid</c> the client must present in assertion headers.</param>
/// <param name="Parameters">RSA public parameters (<c>n</c>/<c>e</c>) - a private copy owned by the caller.</param>
public sealed record ClientAssertionPublicKey(string KeyId, RSAParameters Parameters);
