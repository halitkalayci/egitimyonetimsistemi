namespace Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifies a candidate against a stored hash. Called even when no account was found, so that
    /// the response time does not reveal whether the e-mail exists (spec 0002, AC-29).
    /// </summary>
    bool Verify(string password, string passwordHash);

    /// <summary>
    /// A hash of a random value nobody knows, produced by <see cref="Hash"/> itself and therefore
    /// carrying exactly the current parameters. Verifying against it when no account exists costs
    /// what a real verification costs (spec 0002, AC-29).
    /// </summary>
    string DecoyHash { get; }
}
