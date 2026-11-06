using var key = JwtSigning.CreateRsaKey("consumer-key");

using PubSoft.Authentication.Jwt;
var token = JwtSigning.CreateAccessToken(key, "consumer-issuer", "consumer-api",
    new Dictionary<string, string> { ["sub"] = "consumer-user" }, TimeSpan.FromMinutes(5));
var claims = JwtSigning.ValidateAccessToken(token, "consumer-issuer", "consumer-api", key);
if (claims["sub"] != "consumer-user")
    throw new InvalidOperationException("JWT package consumer check failed.");

Console.WriteLine("Isolated PubSoft.Authentication.Jwt consumer smoke check passed.");