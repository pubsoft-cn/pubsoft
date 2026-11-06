using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Cryptography;

public sealed record DecryptedCredential(
    string Plaintext,
    bool RequiresReEncryption,
    string? ReplacementCiphertext);
