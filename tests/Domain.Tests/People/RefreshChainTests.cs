using Domain.People;

using Shouldly;

namespace Domain.Tests.People;

public sealed class RefreshChainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Person = Guid.CreateVersion7();

    private static RefreshChain Start(int sessionVersion = 1) =>
        RefreshChain.Start(Person, "hash-1", sessionVersion, Now);

    [Fact]
    public void Start_ShouldFixTheWindowAtSevenDays_WhenChainBegins()
    {
        RefreshChain chain = Start();

        chain.ExpiresAtUtc.ShouldBe(Now.AddDays(7));
        chain.IsRevoked.ShouldBeFalse();
        chain.CurrentTokenHash.ShouldBe("hash-1");
    }

    [Fact]
    public void TryRotate_ShouldReplaceTheLiveSecret_WhenCurrentTokenIsPresented()
    {
        RefreshChain chain = Start();

        chain.TryRotate("hash-1", "hash-2", 1, Now.AddMinutes(14)).ShouldBe(RefreshOutcome.Rotated);

        chain.CurrentTokenHash.ShouldBe("hash-2");
        chain.RotatedTokenHashes.ShouldBe(["hash-1"]);
    }

    [Fact]
    public void TryRotate_ShouldRejectThePreviousLink_AfterRotation()
    {
        RefreshChain chain = Start();
        chain.TryRotate("hash-1", "hash-2", 1, Now.AddMinutes(14));

        // spec 0002, AC-11: the spent link must not work any more.
        chain.TryRotate("hash-1", "hash-3", 1, Now.AddMinutes(20)).ShouldBe(RefreshOutcome.Reused);
    }

    [Fact]
    public void TryRotate_ShouldRevokeTheWholeChain_WhenASpentLinkIsPresentedAgain()
    {
        RefreshChain chain = Start();
        chain.TryRotate("hash-1", "hash-2", 1, Now.AddMinutes(14));

        chain.TryRotate("hash-1", "hash-3", 1, Now.AddMinutes(20)).ShouldBe(RefreshOutcome.Reused);

        chain.IsRevoked.ShouldBeTrue();
        chain.RevokedAtUtc.ShouldBe(Now.AddMinutes(20));

        // AC-12: even the newest link - the one the legitimate owner may hold - is now refused.
        chain.TryRotate("hash-2", "hash-4", 1, Now.AddMinutes(21)).ShouldBe(RefreshOutcome.Revoked);
    }

    [Fact]
    public void TryRotate_ShouldReportUnknownToken_WhenSecretNeverBelongedToThisChain()
    {
        RefreshChain chain = Start();

        chain.TryRotate("someone-elses-hash", "hash-2", 1, Now).ShouldBe(RefreshOutcome.UnknownToken);
        chain.IsRevoked.ShouldBeFalse();
        chain.CurrentTokenHash.ShouldBe("hash-1");
    }

    [Fact]
    public void TryRotate_ShouldReject_WhenWindowHasExpired()
    {
        RefreshChain chain = Start();

        chain.TryRotate("hash-1", "hash-2", 1, Now.AddDays(7)).ShouldBe(RefreshOutcome.Expired);
        chain.CurrentTokenHash.ShouldBe("hash-1");
    }

    [Fact]
    public void TryRotate_ShouldNotExtendTheWindow_WhenRotatingRepeatedly()
    {
        RefreshChain chain = Start();

        chain.TryRotate("hash-1", "hash-2", 1, Now.AddDays(3));
        chain.TryRotate("hash-2", "hash-3", 1, Now.AddDays(6));

        // AC-13: seven days from login, not from the last renewal.
        chain.ExpiresAtUtc.ShouldBe(Now.AddDays(7));
        chain.TryRotate("hash-3", "hash-4", 1, Now.AddDays(7).AddSeconds(1)).ShouldBe(RefreshOutcome.Expired);
    }

    [Fact]
    public void TryRotate_ShouldReportStale_WhenAccountSessionVersionMovedOn()
    {
        RefreshChain chain = Start(sessionVersion: 3);

        // AC-16 / AC-17: role change, disabling, password change or logout-all bump the version.
        chain.TryRotate("hash-1", "hash-2", currentAccountSessionVersion: 4, Now.AddMinutes(1))
            .ShouldBe(RefreshOutcome.Stale);

        chain.CurrentTokenHash.ShouldBe("hash-1");
    }

    [Fact]
    public void TryRotate_ShouldReportRevoked_AfterExplicitRevocation()
    {
        RefreshChain chain = Start();
        chain.Revoke(Now.AddMinutes(5));

        chain.TryRotate("hash-1", "hash-2", 1, Now.AddMinutes(6)).ShouldBe(RefreshOutcome.Revoked);
    }

    [Fact]
    public void Revoke_ShouldKeepTheFirstRevocationTime_WhenCalledTwice()
    {
        RefreshChain chain = Start();

        chain.Revoke(Now.AddMinutes(5));
        chain.Revoke(Now.AddMinutes(9));

        chain.RevokedAtUtc.ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public void TryRotate_ShouldCheckReuseBeforeRevocation_WhenBothApply()
    {
        RefreshChain chain = Start();
        chain.TryRotate("hash-1", "hash-2", 1, Now.AddMinutes(1));
        chain.Revoke(Now.AddMinutes(2));

        // The theft signal is the more informative one, so it wins the report.
        chain.TryRotate("hash-1", "hash-9", 1, Now.AddMinutes(3)).ShouldBe(RefreshOutcome.Reused);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void TryRotate_ShouldReject_WhenPresentedHashIsBlank(string presented)
    {
        RefreshChain chain = Start();

        Should.Throw<ArgumentException>(() => chain.TryRotate(presented, "hash-2", 1, Now));
    }

    [Fact]
    public void Start_ShouldReject_WhenTokenHashIsBlank()
    {
        Should.Throw<ArgumentException>(() => RefreshChain.Start(Person, "  ", 1, Now));
    }
}
