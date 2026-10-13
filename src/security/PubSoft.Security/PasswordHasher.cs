using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Security;

/// <summary>Creates and verifies PBKDF2-SHA256 password hashes.</summary>
/// <remarks>
/// Hashes use the format <c>pbkdf2$iterations$saltHex$hashHex</c>. Legacy unsalted
/// SHA-256 hashes can be verified for migration, but should be replaced after a successful login.
/// </remarks>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 2_000_000;

    /// <summary>Iteration count used for newly created hashes.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Verifies a password and optionally supplies a replacement hash for successful legacy or low-cost hashes.</summary>
    public static PasswordVerificationResult VerifyAndRehash(string password, string? storedHash)
    {
        if (!Verify(password, storedHash))
            return new PasswordVerificationResult(false, null);

        return NeedsRehash(storedHash)
            ? new PasswordVerificationResult(true, Hash(password))
            : new PasswordVerificationResult(true, null);
    }

    /// <summary>Hashes a password using PBKDF2-SHA256 with a random salt.</summary>
    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        ValidateIterations(iterations);

        Span<byte> salt = stackalloc byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        CryptographicOperations.ZeroMemory(passwordBytes);
        var encoded = $"{Prefix}${iterations.ToString(CultureInfo.InvariantCulture)}${Convert.ToHexString(salt)}${Convert.ToHexString(derived)}";
        CryptographicOperations.ZeroMemory(derived);
        return encoded;
    }

    /// <summary>Verifies a current PBKDF2 hash or a legacy unsalted SHA-256 hash.</summary>
    public static bool Verify(string password, string? storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (string.IsNullOrEmpty(storedHash))
            return false;

        if (TryReadPbkdf2(storedHash, out var iterations, out var salt, out var expected))
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            CryptographicOperations.ZeroMemory(passwordBytes);
            var isValid = CryptographicOperations.FixedTimeEquals(actual, expected);
            CryptographicOperations.ZeroMemory(actual);
            return isValid;
        }

        return VerifyLegacySha256(password, storedHash);
    }

    /// <summary>Returns whether a hash is legacy, malformed, or below the current work factor.</summary>
    public static bool NeedsRehash(string? storedHash)
    {
        if (!TryReadPbkdf2(storedHash, out var iterations, out _, out _))
            return true;

        return iterations < DefaultIterations;
    }

    private static void ValidateIterations(int iterations)
    {
        if (iterations is < MinimumIterations or > MaximumIterations)
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                $"Iterations must be between {MinimumIterations} and {MaximumIterations}.");
    }

    private static bool TryReadPbkdf2(
        string? storedHash,
        out int iterations,
        out byte[] salt,
        out byte[] expected)
    {
        iterations = 0;
        salt = [];
        expected = [];

        if (string.IsNullOrEmpty(storedHash))
            return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations) ||
            iterations is < MinimumIterations or > MaximumIterations)
            return false;

        try
        {
            salt = Convert.FromHexString(parts[2]);
            expected = Convert.FromHexString(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length == SaltSize && expected.Length == HashSize;
    }

    private static bool VerifyLegacySha256(string password, string storedHash)
    {
        if (storedHash.Length != HashSize * 2)
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var actual = SHA256.HashData(passwordBytes);
        CryptographicOperations.ZeroMemory(passwordBytes);
        var isValid = CryptographicOperations.FixedTimeEquals(actual, expected);
        CryptographicOperations.ZeroMemory(actual);
        return isValid;
    }
}

public readonly record struct PasswordVerificationResult(bool Succeeded, string? ReplacementHash);