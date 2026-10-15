using Domain.People;

using Shouldly;

namespace Domain.Tests.People;

public sealed class UserAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static UserAccount Account(Role role = Role.Participant) =>
        UserAccount.CreateWithTemporaryPassword("  Admin@Immib.ORG ", "hash-v1", role);

    [Fact]
    public void CreateWithTemporaryPassword_ShouldNormaliseEmailAndRequireChange_Always()
    {
        UserAccount account = Account();

        account.Email.ShouldBe("admin@immib.org");
        account.MustChangePassword.ShouldBeTrue();
        account.SessionVersion.ShouldBe(1);
        account.IsDisabled.ShouldBeFalse();
    }

    [Fact]
    public void TryLogin_ShouldReportMustChangePassword_WhenAccountStillCarriesTemporaryPassword()
    {
        UserAccount account = Account();

        account.TryLogin(passwordMatches: true, Now).ShouldBe(LoginOutcome.MustChangePassword);
    }

    [Fact]
    public void TryLogin_ShouldSucceed_WhenPasswordWasAlreadyChanged()
    {
        UserAccount account = Account();
        account.ChangePassword("hash-v2");

        account.TryLogin(passwordMatches: true, Now).ShouldBe(LoginOutcome.Succeeded);
    }

    [Fact]
    public void TryLogin_ShouldCountFailures_WhenPasswordDoesNotMatch()
    {
        UserAccount account = Account();

        account.TryLogin(passwordMatches: false, Now).ShouldBe(LoginOutcome.InvalidCredentials);
        account.FailedAttempts.ShouldBe(1);
        account.IsLockedAt(Now).ShouldBeFalse();
    }

    [Fact]
    public void TryLogin_ShouldLockForFifteenMinutes_WhenFifthAttemptFails()
    {
        UserAccount account = Account();

        for (int attempt = 1; attempt <= UserAccount.MaxFailedAttempts; attempt++)
        {
            account.TryLogin(passwordMatches: false, Now);
        }

        account.IsLockedAt(Now).ShouldBeTrue();
        account.LockedUntilUtc.ShouldBe(Now.AddMinutes(15));
    }

    [Fact]
    public void TryLogin_ShouldRejectCorrectPassword_WhileLocked()
    {
        UserAccount account = Locked();

        account.TryLogin(passwordMatches: true, Now.AddMinutes(5)).ShouldBe(LoginOutcome.Locked);
    }

    [Fact]
    public void TryLogin_ShouldNotExtendLock_WhenAttemptedWhileLocked()
    {
        UserAccount account = Locked();
        DateTimeOffset expectedUntil = account.LockedUntilUtc!.Value;

        account.TryLogin(passwordMatches: false, Now.AddMinutes(14));

        // spec 0002, AC-3: the window is fixed from the first lock, not sliding.
        account.LockedUntilUtc.ShouldBe(expectedUntil);
    }

    [Fact]
    public void TryLogin_ShouldSucceedAndResetCounter_WhenLockWindowHasPassed()
    {
        // The password is changed BEFORE the lock: changing it afterwards would clear the lock by
        // itself and the window would never be tested.
        UserAccount account = Account();
        account.ChangePassword("hash-v2");

        for (int attempt = 1; attempt <= UserAccount.MaxFailedAttempts; attempt++)
        {
            account.TryLogin(passwordMatches: false, Now);
        }

        account.TryLogin(passwordMatches: true, Now.AddMinutes(14)).ShouldBe(LoginOutcome.Locked);
        account.TryLogin(passwordMatches: true, Now.AddMinutes(16)).ShouldBe(LoginOutcome.Succeeded);
        account.FailedAttempts.ShouldBe(0);
        account.LockedUntilUtc.ShouldBeNull();
    }

    [Fact]
    public void TryLogin_ShouldReportDisabled_WhenAccountIsDisabled_EvenWithCorrectPassword()
    {
        UserAccount account = Account();
        account.ChangePassword("hash-v2");
        account.Disable();

        account.TryLogin(passwordMatches: true, Now).ShouldBe(LoginOutcome.Disabled);
    }

    [Fact]
    public void TryLogin_ShouldReportInvalidCredentials_WhenAccountIsDisabledAndPasswordIsWrong()
    {
        UserAccount account = Account();
        account.ChangePassword("hash-v2");
        account.Disable();

        account.TryLogin(passwordMatches: false, Now).ShouldBe(LoginOutcome.InvalidCredentials);
    }

    [Fact]
    public void TryLogin_ShouldReportDisabledBeforeLocked_WhenAccountIsBoth()
    {
        UserAccount account = Locked();
        account.Disable();

        account.TryLogin(passwordMatches: true, Now).ShouldBe(LoginOutcome.Disabled);
    }

    [Fact]
    public void ChangePassword_ShouldClearTemporaryFlagAndBumpSessionVersion_WhenChanged()
    {
        UserAccount account = Account();
        int before = account.SessionVersion;

        account.ChangePassword("hash-v2");

        account.PasswordHash.ShouldBe("hash-v2");
        account.MustChangePassword.ShouldBeFalse();
        account.SessionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public void ResetToTemporaryPassword_ShouldRequireChangeAgainAndBumpSessionVersion_WhenAdminResets()
    {
        UserAccount account = Account();
        account.ChangePassword("hash-v2");
        int before = account.SessionVersion;

        account.ResetToTemporaryPassword("hash-temp");

        account.MustChangePassword.ShouldBeTrue();
        account.SessionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public void AssignRole_ShouldBumpSessionVersion_WhenRoleActuallyChanges()
    {
        UserAccount account = Account(Role.Participant);
        int before = account.SessionVersion;

        account.AssignRole(Role.Instructor);

        account.Role.ShouldBe(Role.Instructor);
        account.SessionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public void AssignRole_ShouldLeaveSessionsAlone_WhenRoleIsUnchanged()
    {
        UserAccount account = Account(Role.Instructor);
        int before = account.SessionVersion;

        account.AssignRole(Role.Instructor);

        account.SessionVersion.ShouldBe(before);
    }

    [Fact]
    public void Disable_ShouldBumpSessionVersionOnce_WhenCalledRepeatedly()
    {
        UserAccount account = Account();
        int before = account.SessionVersion;

        account.Disable();
        account.Disable();

        account.SessionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public void Enable_ShouldClearLockAndBumpSessionVersion_WhenReEnabling()
    {
        UserAccount account = Locked();
        account.Disable();
        int before = account.SessionVersion;

        account.Enable();

        account.IsDisabled.ShouldBeFalse();
        account.LockedUntilUtc.ShouldBeNull();
        account.SessionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public void EndAllSessions_ShouldBumpSessionVersion_WithoutTouchingThePassword()
    {
        UserAccount account = Account();
        string hash = account.PasswordHash;
        int before = account.SessionVersion;

        account.EndAllSessions();

        account.SessionVersion.ShouldBe(before + 1);
        account.PasswordHash.ShouldBe(hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWithTemporaryPassword_ShouldReject_WhenEmailIsBlank(string email)
    {
        Should.Throw<ArgumentException>(() =>
            UserAccount.CreateWithTemporaryPassword(email, "hash", Role.Participant));
    }

    [Fact]
    public void ChangePassword_ShouldReject_WhenHashIsBlank()
    {
        UserAccount account = Account();

        Should.Throw<ArgumentException>(() => account.ChangePassword("   "));
    }

    private static UserAccount Locked()
    {
        UserAccount account = Account();

        for (int attempt = 1; attempt <= UserAccount.MaxFailedAttempts; attempt++)
        {
            account.TryLogin(passwordMatches: false, Now);
        }

        return account;
    }
}
