using System.Security.Cryptography;

using Application.Abstractions;

namespace Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 from the BCL - no third party dependency, which keeps `docs/security.md`'s
/// ADR requirement for new packages out of the critical path (ADR-0012). The stored value carries
/// its own parameters (`v1$iterations$salt$hash`), so the work factor can be raised later and old
/// hashes keep verifying.
/// </summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Version = "v1";
    private const int Iterations = 210_000;
    private const int SaltSizeInBytes = 16;
    private const int HashSizeInBytes = 32;

    /// <summary>Registered as a singleton, so this is computed once per process.</summary>
    public Pbkdf2PasswordHasher() =>
        DecoyHash = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public string DecoyHash { get; }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeInBytes);
        byte[] hash = Derive(password, salt, Iterations, HashSizeInBytes);

        return string.Join('$', Version, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string passwordHash)
    {
        if (password is null || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        string[] parts = passwordHash.Split('$');

        if (parts.Length != 4 || parts[0] != Version || !int.TryParse(parts[1], out int iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Derive(password, salt, iterations, expected.Length);

        // Fixed-time comparison: a timing difference here would leak how much of the hash matched.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);
}
