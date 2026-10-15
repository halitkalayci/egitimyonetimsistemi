namespace Application.Abstractions;

public sealed record RefreshTokenPair(string Secret, string Hash);

public interface IRefreshTokenGenerator
{
    /// <summary>
    /// Produces a cryptographically random secret for the client and the hash we persist. The raw
    /// secret never reaches the database; the hash never reaches the client.
    /// </summary>
    RefreshTokenPair Create();

    string HashOf(string secret);
}
