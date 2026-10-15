namespace Domain.People;

/// <summary>
/// Why a login attempt ended the way it did. The HTTP boundary deliberately collapses
/// <see cref="InvalidCredentials"/> for unknown e-mail and wrong password into one response
/// (spec 0002, AC-2) - the distinction exists here, not in the answer sent to the caller.
/// </summary>
public enum LoginOutcome
{
    Succeeded,
    InvalidCredentials,
    Locked,
    Disabled,

    /// <summary>Credentials were right but the account still carries a temporary password.</summary>
    MustChangePassword
}

/// <summary>
/// The optional account part of a person record (docs/domain.md, tek kayıt modeli): a person
/// without one simply cannot log in. The account never sees a plaintext password - hashing is a
/// port implemented in Infrastructure, so this type stores and compares opaque hashes only.
/// </summary>
public sealed class UserAccount
{
    /// <summary>docs/security.md: 5 failed attempts lock the account for 15 minutes.</summary>
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    /// <summary>spec 0002, AC-8.</summary>
    public const int MinimumPasswordLength = 10;

    /// <summary>Column size of the e-mail; checked before saving so it is a user error, not a crash.</summary>
    public const int MaxEmailLength = 256;

    /// <summary>Materialisation only (ORM); real creation goes through the factory methods.</summary>
    private UserAccount()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    private UserAccount(string email, string passwordHash, Role role, bool mustChangePassword)
    {
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        MustChangePassword = mustChangePassword;
        SessionVersion = 1;
    }

    /// <summary>Normalised (trimmed, lower-cased) and unique system wide (docs/domain.md).</summary>
    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public Role Role { get; private set; }

    public bool IsDisabled { get; private set; }

    /// <summary>Set while the account still carries the temporary password it was created with.</summary>
    public bool MustChangePassword { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    /// <summary>
    /// Bumped whenever existing sessions must stop being renewable: password change, role change,
    /// disabling, logout-all. Refresh chains carry the version they were issued under.
    /// </summary>
    public int SessionVersion { get; private set; }

    public static UserAccount CreateWithTemporaryPassword(string email, string temporaryPasswordHash, Role role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPasswordHash);

        return new UserAccount(NormaliseEmail(email), temporaryPasswordHash, role, mustChangePassword: true);
    }

    public static UserAccount FromStorage(
        string email,
        string passwordHash,
        Role role,
        bool isDisabled,
        bool mustChangePassword,
        int failedAttempts,
        DateTimeOffset? lockedUntilUtc,
        int sessionVersion) =>
        new(email, passwordHash, role, mustChangePassword)
        {
            IsDisabled = isDisabled,
            FailedAttempts = failedAttempts,
            LockedUntilUtc = lockedUntilUtc?.ToUniversalTime(),
            SessionVersion = sessionVersion
        };

    public static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    public bool IsLockedAt(DateTimeOffset nowUtc) => LockedUntilUtc > nowUtc;

    /// <summary>
    /// The single place that decides a login's fate. The caller has already verified the password
    /// against <see cref="PasswordHash"/> and passes the verdict in, because verification needs a
    /// hashing algorithm and the domain owns no infrastructure.
    /// </summary>
    public LoginOutcome TryLogin(bool passwordMatches, DateTimeOffset nowUtc)
    {
        if (IsDisabled)
        {
            // "Disabled" is only said to someone who knows the password; anyone else gets the
            // answer an unknown e-mail gets, so one request cannot reveal that the account exists.
            return passwordMatches ? LoginOutcome.Disabled : LoginOutcome.InvalidCredentials;
        }

        if (IsLockedAt(nowUtc))
        {
            // An attempt made while locked does NOT extend the lock (spec 0002, AC-3).
            return LoginOutcome.Locked;
        }

        if (!passwordMatches)
        {
            RegisterFailedAttempt(nowUtc);
            return LoginOutcome.InvalidCredentials;
        }

        FailedAttempts = 0;
        LockedUntilUtc = null;

        return MustChangePassword ? LoginOutcome.MustChangePassword : LoginOutcome.Succeeded;
    }

    public void ChangePassword(string newPasswordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);

        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        FailedAttempts = 0;
        LockedUntilUtc = null;
        SessionVersion++;
    }

    /// <summary>An administrator hands out a new temporary password; the user must replace it.</summary>
    public void ResetToTemporaryPassword(string temporaryPasswordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPasswordHash);

        PasswordHash = temporaryPasswordHash;
        MustChangePassword = true;
        FailedAttempts = 0;
        LockedUntilUtc = null;
        SessionVersion++;
    }

    public void AssignRole(Role role)
    {
        if (Role == role)
        {
            return;
        }

        Role = role;
        SessionVersion++;
    }

    public void Disable()
    {
        if (IsDisabled)
        {
            return;
        }

        IsDisabled = true;
        SessionVersion++;
    }

    public void Enable()
    {
        if (!IsDisabled)
        {
            return;
        }

        IsDisabled = false;
        FailedAttempts = 0;
        LockedUntilUtc = null;
        SessionVersion++;
    }

    /// <summary>Invalidates every renewable session without touching the password.</summary>
    public void EndAllSessions() => SessionVersion++;

    private void RegisterFailedAttempt(DateTimeOffset nowUtc)
    {
        FailedAttempts++;

        if (FailedAttempts >= MaxFailedAttempts)
        {
            LockedUntilUtc = nowUtc.ToUniversalTime().Add(LockDuration);
            FailedAttempts = 0;
        }
    }
}
