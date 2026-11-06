using System.Security.Cryptography;
using System.Text;
using PubSoft.Security;

var passwordHash = PasswordHasher.Hash("consumer-smoke", 100_000);
if (!PasswordHasher.Verify("consumer-smoke", passwordHash) || PasswordHasher.Verify("wrong", passwordHash))
    throw new InvalidOperationException("Password hashing consumer check failed.");

var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("legacy-consumer")));
var upgraded = PasswordHasher.VerifyAndRehash("legacy-consumer", legacyHash);
if (!upgraded.Succeeded || upgraded.ReplacementHash is null || PasswordHasher.NeedsRehash(upgraded.ReplacementHash))
    throw new InvalidOperationException("Password rehash consumer check failed.");

var masking = new DataMaskingService();
if (masking.MaskPhoneNumber("1234567890") != "******7890")
    throw new InvalidOperationException("Data masking consumer check failed.");

var limiter = new RateLimiter(new RateLimiterOptions { RequestsPerWindow = 1 });
if (!limiter.CheckRateLimit("consumer").IsAllowed || limiter.CheckRateLimit("consumer").IsAllowed)
    throw new InvalidOperationException("Rate limiter consumer check failed.");

var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
var signature = HmacSignatureValidator.BuildSignature("secret", "GET", "/", timestamp, "nonce");
var validator = new HmacSignatureValidator(new MemoryNonceStore());
var valid = await validator.ValidateAsync("consumer", signature, timestamp, "nonce", "GET", "/", null, null, "secret");
var replay = await validator.ValidateAsync("consumer", signature, timestamp, "nonce", "GET", "/", null, null, "secret");
if (!valid.IsValid || replay.Error != "nonce_replayed")
    throw new InvalidOperationException("HMAC nonce consumer check failed.");

Console.WriteLine("Isolated PubSoft.Security consumer smoke check passed.");