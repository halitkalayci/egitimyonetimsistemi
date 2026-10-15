using System.Security.Cryptography;
using System.Text;

using Application.Abstractions;

namespace Infrastructure.Security;

/// <summary>
/// The client gets 256 bits of randomness; the database only ever sees its SHA-256. A stolen
/// database therefore yields no usable refresh token. Hashing is unsalted and unstretched on
/// purpose: the input is already high-entropy random, so the lookup stays a single indexed read.
/// </summary>
internal sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private const int SecretSizeInBytes = 32;

    public RefreshTokenPair Create()
    {
        string secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretSizeInBytes));

        return new RefreshTokenPair(secret, HashOf(secret));
    }

    public string HashOf(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
