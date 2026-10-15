using Application.Abstractions;
using Application.Common;
using Application.Identity;

using Domain.People;

using NSubstitute;

using Shouldly;

namespace Application.Tests.Identity;

public sealed class SessionLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly IParticipantRepository _participants = Substitute.For<IParticipantRepository>();
    private readonly IRefreshChainRepository _chains = Substitute.For<IRefreshChainRepository>();
    private readonly ITokenIssuer _tokens = Substitute.For<ITokenIssuer>();
    private readonly IRefreshTokenGenerator _refresh = Substitute.For<IRefreshTokenGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public SessionLifecycleTests()
    {
        _clock.UtcNow.Returns(Now);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(1));
        _tokens.IssueAccessToken(Arg.Any<Guid>(), Arg.Any<Role>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool>())
            .Returns(new AccessToken("new-access", Now.AddMinutes(15)));
        _refresh.HashOf(Arg.Any<string>()).Returns(call => $"hash-of-{call.Arg<string>()}");
        _refresh.Create().Returns(new RefreshTokenPair("secret-2", "hash-of-secret-2"));
    }

    [Fact]
    public async Task Refresh_ShouldIssueNewAccessAndRefreshToken_WhenSecretIsCurrent()
    {
        (Participant person, RefreshChain chain) = GivenActiveSession();

        Result<AuthenticationResultDto> result = await Refresh("secret-1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("new-access");
        result.Value.RefreshToken.ShouldBe("secret-2");
        chain.CurrentTokenHash.ShouldBe("hash-of-secret-2");
        person.Account!.SessionVersion.ShouldBe(chain.AccountSessionVersion);
    }

    [Fact]
    public async Task Refresh_ShouldNotExtendTheWindow_WhenRotating()
    {
        (_, RefreshChain chain) = GivenActiveSession();

        Result<AuthenticationResultDto> result = await Refresh("secret-1");

        result.Value.RefreshTokenExpiresAtUtc.ShouldBe(chain.StartedAtUtc.AddDays(7));
    }

    [Fact]
    public async Task Refresh_ShouldFail_WhenSecretIsUnknown()
    {
        _chains.FindByTokenHashAsync("hash-of-nope", Arg.Any<CancellationToken>())
            .Returns(Result<RefreshChain?>.Success(null));

        Result<AuthenticationResultDto> result = await Refresh("nope");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("auth.session_not_renewable");
    }

    [Fact]
    public async Task Refresh_ShouldFailAndRevokeChain_WhenAccountWasDisabled()
    {
        (Participant person, RefreshChain chain) = GivenActiveSession();
        person.Account!.Disable();

        Result<AuthenticationResultDto> result = await Refresh("secret-1");

        // AC-17: disabling must stop renewal at once, not when the window ends.
        result.Error!.Code.ShouldBe("auth.session_not_renewable");
        chain.IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_ShouldFail_WhenRoleChangedSinceTheChainStarted()
    {
        (Participant person, _) = GivenActiveSession();
        person.Account!.AssignRole(Role.Admin);

        Result<AuthenticationResultDto> result = await Refresh("secret-1");

        // AC-16: the chain is stale because the account's session version moved on.
        result.Error!.Code.ShouldBe("auth.session_not_renewable");
    }

    [Fact]
    public async Task Refresh_ShouldFailWithTheSameError_WhenSecretIsReused()
    {
        (_, RefreshChain chain) = GivenActiveSession();
        await Refresh("secret-1");
        _refresh.Create().Returns(new RefreshTokenPair("secret-3", "hash-of-secret-3"));

        Result<AuthenticationResultDto> result = await Refresh("secret-1");

        result.Error!.Code.ShouldBe("auth.session_not_renewable");
        chain.IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_ShouldFail_WhenRefreshTokenIsBlank()
    {
        Result<AuthenticationResultDto> result = await Refresh("   ");

        result.Error!.Code.ShouldBe("auth.session_not_renewable");
        await _chains.DidNotReceive().FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_ShouldCarryTheMustChangePasswordFlag_WhenAccountStillHasTemporaryPassword()
    {
        (Participant person, _) = GivenActiveSession(changed: false);

        Result<AuthenticationResultDto> result = await Refresh("secret-1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.MustChangePassword.ShouldBeTrue();
        person.Account!.MustChangePassword.ShouldBeTrue();
    }

    [Fact]
    public async Task Logout_ShouldRevokeOnlyThePresentedChain_WhenTwoSessionsExist()
    {
        (Participant person, RefreshChain phone) = GivenActiveSession();
        RefreshChain desktop = RefreshChain.Start(person.Id, "hash-of-desktop", 1, Now);
        _chains.FindByTokenHashAsync("hash-of-desktop", Arg.Any<CancellationToken>())
            .Returns(Result<RefreshChain?>.Success(desktop));

        Result<bool> result = await new LogoutCommandHandler(_chains, _refresh, _unitOfWork, _clock)
            .HandleAsync(new LogoutCommand("secret-1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        phone.IsRevoked.ShouldBeTrue();
        desktop.IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task Logout_ShouldSucceedSilently_WhenSecretIsUnknown()
    {
        _chains.FindByTokenHashAsync("hash-of-nope", Arg.Any<CancellationToken>())
            .Returns(Result<RefreshChain?>.Success(null));

        Result<bool> result = await new LogoutCommandHandler(_chains, _refresh, _unitOfWork, _clock)
            .HandleAsync(new LogoutCommand("nope"), TestContext.Current.CancellationToken);

        // Answering "not found" here would turn logout into an oracle for valid secrets.
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task LogoutAll_ShouldRevokeEveryChainAndBumpSessionVersion_WhenCalled()
    {
        (Participant person, RefreshChain phone) = GivenActiveSession();
        RefreshChain desktop = RefreshChain.Start(person.Id, "hash-of-desktop", 1, Now);
        _chains.ListActiveForParticipantAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<RefreshChain>>.Success([phone, desktop]));
        int before = person.Account!.SessionVersion;

        Result<bool> result = await new LogoutAllCommandHandler(_participants, _chains, _unitOfWork, _clock)
            .HandleAsync(new LogoutAllCommand(person.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        phone.IsRevoked.ShouldBeTrue();
        desktop.IsRevoked.ShouldBeTrue();
        person.Account.SessionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public async Task LogoutAll_ShouldFail_WhenPersonHasNoAccount()
    {
        Participant person = Participant.CreateWithoutAccount("Hesapsız", null);
        _participants.FindByIdAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(person));

        Result<bool> result = await new LogoutAllCommandHandler(_participants, _chains, _unitOfWork, _clock)
            .HandleAsync(new LogoutAllCommand(person.Id), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.no_account");
    }

    [Fact]
    public async Task Refresh_ShouldEndInARevokedChain_WhenItLosesARaceForTheSameSecret()
    {
        (Participant person, RefreshChain staleView) = GivenActiveSession();

        // What the database holds once the winner committed: secret-1 is already spent.
        RefreshChain committed = RefreshChain.FromStorage(
            staleView.Id, person.Id, "hash-of-winner", staleView.AccountSessionVersion,
            staleView.StartedAtUtc, staleView.ExpiresAtUtc, revokedAtUtc: null, ["hash-of-secret-1"]);
        _chains.FindByTokenHashAsync("hash-of-secret-1", Arg.Any<CancellationToken>())
            .Returns(Result<RefreshChain?>.Success(staleView), Result<RefreshChain?>.Success(committed));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(PersistenceErrors.ConcurrentUpdate), Result<int>.Success(1));

        Result<AuthenticationResultDto> result = await Refresh("secret-1");

        // AC-12 under concurrency: the loser sees its secret among the spent links and burns the chain.
        _unitOfWork.Received(1).DiscardChanges();
        result.Error!.Code.ShouldBe("auth.session_not_renewable");
        committed.IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_ShouldRefuse_WhenItLosesTheRaceTwice()
    {
        GivenActiveSession();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(PersistenceErrors.ConcurrentUpdate));

        Result<AuthenticationResultDto> result = await Refresh("secret-1");

        result.Error!.Code.ShouldBe("auth.session_not_renewable");
    }

    private Task<Result<AuthenticationResultDto>> Refresh(string secret) =>
        new RefreshSessionCommandHandler(_participants, _chains, _tokens, _refresh, _unitOfWork, _clock)
            .HandleAsync(new RefreshSessionCommand(secret), TestContext.Current.CancellationToken);

    private (Participant Person, RefreshChain Chain) GivenActiveSession(bool changed = true)
    {
        Participant person = Participant.CreateWithAccount(
            "Ayşe Yılmaz", Guid.CreateVersion7(), "ayse@firma.com", "stored-hash", Role.Participant);

        if (changed)
        {
            person.Account!.ChangePassword("stored-hash");
        }

        RefreshChain chain = RefreshChain.Start(
            person.Id, "hash-of-secret-1", person.Account!.SessionVersion, Now);

        _participants.FindByIdAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(person));
        _chains.FindByTokenHashAsync("hash-of-secret-1", Arg.Any<CancellationToken>())
            .Returns(Result<RefreshChain?>.Success(chain));

        return (person, chain);
    }
}
