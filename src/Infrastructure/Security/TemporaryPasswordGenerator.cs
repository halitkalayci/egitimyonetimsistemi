using System.Security.Cryptography;

using Application.Abstractions;

namespace Infrastructure.Security;

/// <summary>
/// A readable random password that satisfies the policy. The alphabet leaves out characters that
/// are easy to confuse when the password is read out or copied by hand (O/0, l/1, I).
/// </summary>
internal sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
    private const int Length = 14;

    public string Create() => RandomNumberGenerator.GetString(Alphabet, Length);
}
