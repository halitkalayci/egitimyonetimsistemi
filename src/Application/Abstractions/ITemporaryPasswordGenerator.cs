namespace Application.Abstractions;

public interface ITemporaryPasswordGenerator
{
    /// <summary>
    /// A cryptographically random password handed out once when an account is created or reset.
    /// It satisfies the password policy and is shown to the administrator a single time.
    /// </summary>
    string Create();
}
